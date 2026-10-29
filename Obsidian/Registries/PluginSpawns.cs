using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API.Entities;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Entities;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData;
using System.Collections.Frozen;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// The biomes' natural mob spawns: vanilla's biomes', plugins' biomes' own <c>spawners</c>, <c>spawn_costs</c> and
/// <c>creature_spawn_probability</c>, and plugins' biome modifiers' spawners and spawn costs applied to both.
/// </summary>
/// <remarks>
/// Like features, spawns aren't sent to clients or saved: each <see cref="Apply"/> builds them again from the loaded
/// plugins' files, and the mob spawner reads <see cref="Current"/> for each group it spawns.
/// </remarks>
internal static partial class PluginSpawns
{
    private static readonly Lock applying = new();

    private static BiomeSpawns? current;

    /// <summary>
    /// The spawns natural spawning uses: vanilla's with the loaded plugins' files applied.
    /// </summary>
    public static BiomeSpawns Current => Volatile.Read(ref current) ?? BiomeSpawns.Vanilla;

    /// <summary>
    /// Makes the spawns of the loaded plugins' biomes and biome modifiers the <see cref="Current"/> ones.
    /// </summary>
    /// <param name="files">The loaded plugins' feature and biome modifier files, in load order.</param>
    /// <param name="biomes">The loaded plugins' codec files, in load order, whose biomes' spawners are used.</param>
    public static void Apply(IEnumerable<FeatureFile> files, IEnumerable<CodecFile> biomes, ILogger logger)
    {
        lock (applying)
            Volatile.Write(ref current, Build(files, biomes, logger));
    }

    /// <summary>
    /// Builds the spawns of plugins' biomes and biome modifiers, without making them the current ones. Modifiers
    /// that can't be applied are logged and left out.
    /// </summary>
    public static BiomeSpawns Build(IEnumerable<FeatureFile> files, IEnumerable<CodecFile> biomes, ILogger logger)
    {
        var vanilla = BiomeSpawns.Vanilla;
        var lists = new Dictionary<string, IReadOnlyDictionary<string, SpawnerMob[]>>(vanilla.Biomes);
        var costs = new Dictionary<string, IReadOnlyDictionary<string, SpawnCost>>(vanilla.Costs);
        var probabilities = new Dictionary<string, float>(vanilla.CreatureProbabilities);
        var changed = false;

        // The last file in order defining a biome or modifier wins, as with features; a file for one of vanilla's
        // biomes replaces its spawners and spawn costs. PluginCodecs and PluginFeatures log the files replaced.
        var pluginBiomes = PluginCodecs.Winners(biomes, NullLogger.Instance)
            .Where(file => file.Registry == PluginCodecs.Biome);

        foreach (var biome in pluginBiomes)
        {
            var element = (BiomeElement)biome.Element;

            lists[biome.Identifier] = element.Spawners;
            costs[biome.Identifier] = element.SpawnCosts;
            probabilities[biome.Identifier] = element.CreatureSpawnProbability;
            changed = true;
        }

        var modifiers = VanillaOverrides
            .LastWins(
                files.Where(file => file.Registry == PluginFeatures.BiomeModifier),
                file => (file.Registry, file.Identifier),
                file => file.Source,
                NullLogger.Instance)
            .Where(file => file.Modifier is not null)
            .ToList();

        // As with features, every modifier's additions are applied before any modifier's removals, each in load
        // order. What a modifier names is resolved first, and its additions are applied whole or not at all, so one
        // that can't be applied leaves out its spawners and spawn costs, in both phases.
        var resolved = new List<(BiomeModifierData Modifier, List<string> Biomes, HashSet<int> Spawners,
            HashSet<int> Costs, FeatureFile File)>();

        foreach (var file in modifiers)
        {
            try
            {
                var modifier = file.Modifier!;

                resolved.Add((
                    modifier,
                    PluginFeatures.ResolveBiomes(modifier),
                    EntityTypeIds(modifier.Remove.Spawners, "remove.spawners"),
                    EntityTypeIds(modifier.Remove.SpawnCosts, "remove.spawn_costs"),
                    file));
            }
            catch (InvalidDataException ex)
            {
                Log.Invalid(logger, file.Source, ex.Message);
            }
        }

        var failed = new HashSet<FeatureFile>();

        foreach (var (modifier, changedBiomes, _, _, file) in resolved)
        {
            try
            {
                AddSpawns(lists, changedBiomes, modifier.Add.Spawners);
                AddCosts(costs, changedBiomes, modifier.Add.SpawnCosts);
                changed = true;
            }
            catch (InvalidDataException ex)
            {
                failed.Add(file);
                Log.Invalid(logger, file.Source, ex.Message);
            }
        }

        foreach (var (_, changedBiomes, spawners, spawnCosts, file) in resolved)
        {
            if (failed.Contains(file))
                continue;

            RemoveSpawns(lists, changedBiomes, spawners);
            RemoveCosts(costs, changedBiomes, spawnCosts);
            changed = true;
        }

        if (!changed)
            return vanilla;

        return new BiomeSpawns(lists.ToFrozenDictionary())
        {
            Costs = costs.ToFrozenDictionary(),
            CreatureProbabilities = probabilities.ToFrozenDictionary()
        };
    }

    /// <summary>
    /// The category of an entity type's mobs, by the type's identifier: a plugin type's <c>Category</c>, or a vanilla
    /// type's own (see <see cref="MobSpawner.SpawnCategory(EntityType?)"/>).
    /// </summary>
    public static string CategoryOf(string entityType)
    {
        if (EntityNbt.TryParseType(entityType, out var vanilla))
            return MobSpawner.SpawnCategory(vanilla);

        return EntityDefinitionsRegistry.TryGet(entityType, out var definition)
            ? Name(definition.Settings.Category)
            : "misc";
    }

    /// <summary>
    /// Vanilla's name of a mob category, like <c>water_creature</c>.
    /// </summary>
    public static string Name(MobCategory category) => category.ToString().ToSnakeCase();

    // Each spawner goes to the end of its entity type's category's list. A modifier that would make a list's weights
    // add up to more than vanilla's WeightedList holds (an int) is left out whole.
    private static void AddSpawns(
        Dictionary<string, IReadOnlyDictionary<string, SpawnerMob[]>> lists,
        List<string> biomes,
        ImmutableArray<SpawnerMob> added)
    {
        if (added.IsEmpty)
            return;

        var updated = new Dictionary<string, IReadOnlyDictionary<string, SpawnerMob[]>>();

        foreach (var biome in biomes)
        {
            var categories = lists.GetValueOrDefault(biome)?.ToDictionary() ?? [];

            foreach (var spawner in added)
            {
                var category = CategoryOf(spawner.Type);
                SpawnerMob[] spawners = [.. categories.GetValueOrDefault(category) ?? [], spawner];

                if (spawners.Sum(mob => (long)mob.Weight) > int.MaxValue)
                {
                    throw new InvalidDataException(
                        $"add.spawners: the {category} weights of {biome} would add up to more than {int.MaxValue}");
                }

                categories[category] = spawners;
            }

            updated[biome] = categories;
        }

        foreach (var (biome, categories) in updated)
            lists[biome] = categories;
    }

    // The entity types' spawners are removed from every category.
    private static void RemoveSpawns(
        Dictionary<string, IReadOnlyDictionary<string, SpawnerMob[]>> lists,
        List<string> biomes,
        HashSet<int> removed)
    {
        if (removed.Count == 0)
            return;

        foreach (var biome in biomes)
        {
            if (lists.GetValueOrDefault(biome) is not IReadOnlyDictionary<string, SpawnerMob[]> categories)
                continue;

            lists[biome] = categories.ToDictionary(
                category => category.Key,
                category => category.Value
                    .Where(spawner => PluginTags.EntityTypeId(spawner.Type) is not int id || !removed.Contains(id))
                    .ToArray());
        }
    }

    // An added cost replaces the entity type's cost in the biomes, like a later entry of vanilla's spawn_costs map.
    private static void AddCosts(
        Dictionary<string, IReadOnlyDictionary<string, SpawnCost>> costs,
        List<string> biomes,
        ImmutableDictionary<string, SpawnCost> added)
    {
        if (added.IsEmpty)
            return;

        foreach (var biome in biomes)
        {
            var biomeCosts = costs.GetValueOrDefault(biome)?.ToDictionary() ?? [];

            foreach (var (type, cost) in added)
                biomeCosts[type] = cost;

            costs[biome] = biomeCosts;
        }
    }

    private static void RemoveCosts(
        Dictionary<string, IReadOnlyDictionary<string, SpawnCost>> costs,
        List<string> biomes,
        HashSet<int> removed)
    {
        if (removed.Count == 0)
            return;

        foreach (var biome in biomes)
        {
            if (costs.GetValueOrDefault(biome) is not IReadOnlyDictionary<string, SpawnCost> biomeCosts)
                continue;

            costs[biome] = biomeCosts
                .Where(cost => PluginTags.EntityTypeId(cost.Key) is not int id || !removed.Contains(id))
                .ToDictionary();
        }
    }

    // A set of entity types to their ids: identifiers and #tags.
    private static HashSet<int> EntityTypeIds(ImmutableArray<string> types, string path)
    {
        var ids = new HashSet<int>();

        foreach (var type in types)
        {
            if (!type.StartsWith('#'))
            {
                if (PluginTags.EntityTypeId(type) is int id)
                    ids.Add(id);

                continue;
            }

            if (!TagsRegistry.TryGet("entity_type", type[1..], out var tag))
                throw new InvalidDataException($"{path}: {type} isn't an entity type tag");

            ids.UnionWith(tag.Entries);
        }

        return ids;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Left out {Source}: {Message}")]
        public static partial void Invalid(ILogger logger, string source, string message);
    }
}

/// <summary>
/// The spawners of each biome, by biome identifier and mob category (vanilla's names, like <c>monster</c>), with the
/// biomes' spawn costs and creature spawn probabilities.
/// </summary>
internal sealed record BiomeSpawns(IReadOnlyDictionary<string, IReadOnlyDictionary<string, SpawnerMob[]>> Biomes)
{
    /// <summary>
    /// Vanilla's chance of each further group of creatures spawning when a chunk generates, for biomes that don't set
    /// their own.
    /// </summary>
    public const float DefaultCreatureProbability = 0.1f;

    /// <summary>
    /// Vanilla's biomes' spawners, from Codecs/biomes.json.
    /// </summary>
    public static BiomeSpawns Vanilla { get; } = LoadVanilla();

    /// <summary>
    /// The biomes' spawn costs, by biome identifier and entity type identifier.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, SpawnCost>> Costs { get; init; } =
        FrozenDictionary<string, IReadOnlyDictionary<string, SpawnCost>>.Empty;

    /// <summary>
    /// The biomes' creature spawn probabilities, by biome identifier, for biomes that don't use
    /// <see cref="DefaultCreatureProbability"/>.
    /// </summary>
    public IReadOnlyDictionary<string, float> CreatureProbabilities { get; init; } =
        FrozenDictionary<string, float>.Empty;

    /// <summary>
    /// The cost of spawning an entity type in a biome, or null when spawning it costs nothing there.
    /// </summary>
    public SpawnCost? CostOf(string biome, string entityType) =>
        this.Costs.GetValueOrDefault(biome)?.GetValueOrDefault(entityType);

    /// <summary>
    /// The chance of each further group of creatures spawning in a biome when a chunk generates.
    /// </summary>
    public float CreatureProbabilityOf(string biome) =>
        this.CreatureProbabilities.GetValueOrDefault(biome, DefaultCreatureProbability);

    private static BiomeSpawns LoadVanilla()
    {
        // The legacy version's (Assets/legacy-1.21.11/biome_spawns.json), since 26.1 moved spawns out of the biome and
        // worldgen runs on the legacy version's biomes for now.
        var resource = "Obsidian.Assets.biome_spawns.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidDataException("Missing biome spawn data.");
        using var document = JsonDocument.Parse(stream);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        var spawners = new Dictionary<string, IReadOnlyDictionary<string, SpawnerMob[]>>();
        var costs = new Dictionary<string, IReadOnlyDictionary<string, SpawnCost>>();
        var probabilities = new Dictionary<string, float>();

        // In the file's order, which MobSpawner's vanilla mob categories depend on.
        foreach (var entry in document.RootElement.GetProperty("value").EnumerateArray())
        {
            var name = entry.GetProperty("name").GetString()!;
            var element = entry.GetProperty("element");

            spawners[name] = element.GetProperty("spawners").Deserialize<Dictionary<string, SpawnerMob[]>>(options)!;

            if (element.TryGetProperty("spawn_costs", out var spawnCosts))
                costs[name] = spawnCosts.Deserialize<Dictionary<string, SpawnCost>>(options)!;

            if (element.TryGetProperty("creature_spawn_probability", out var probability))
                probabilities[name] = probability.GetSingle();
        }

        return new BiomeSpawns(spawners)
        {
            Costs = costs.ToFrozenDictionary(),
            CreatureProbabilities = probabilities.ToFrozenDictionary()
        };
    }
}
