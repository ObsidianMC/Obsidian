using Microsoft.Extensions.Logging;
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
/// The biomes' natural mob spawns: vanilla's biomes', plugins' biomes' own <c>spawners</c>, and NeoForge's
/// <c>neoforge:add_spawns</c> and <c>neoforge:remove_spawns</c> biome modifiers applied to both.
/// </summary>
/// <remarks>
/// Like features, spawns aren't sent to clients or saved: each <see cref="Apply"/> builds them again from the loaded
/// plugins' files, and the mob spawner reads <see cref="Current"/> for each group it spawns. Spawn costs, vanilla's
/// included, aren't used by Obsidian's spawner, so spawn cost modifiers change nothing.
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
        var lists = new Dictionary<string, IReadOnlyDictionary<string, SpawnerMob[]>>(BiomeSpawns.Vanilla.Biomes);
        var changed = false;

        // The first plugin in load order defining a biome or modifier wins, as with features.
        var pluginBiomes = biomes
            .Where(file => file.Registry == PluginCodecs.Biome)
            .DistinctBy(file => file.Identifier);

        foreach (var biome in pluginBiomes)
        {
            lists[biome.Identifier] = ((BiomeElement)biome.Element).Spawners;
            changed = true;
        }

        var modifiers = files
            .Where(file => file.Registry == PluginFeatures.BiomeModifier)
            .DistinctBy(file => file.Identifier)
            .Where(file => file.Modifier?.Type is BiomeModifierData.AddSpawns or BiomeModifierData.RemoveSpawns)
            .ToList();

        // Like NeoForge's phases, every modifier adding spawns runs before any removing them, each in load order.
        var phases = modifiers
            .Where(file => file.Modifier!.Adds)
            .Concat(modifiers.Where(file => !file.Modifier!.Adds));

        foreach (var file in phases)
        {
            try
            {
                if (file.Modifier!.Type == BiomeModifierData.AddSpawns)
                    AddSpawns(lists, file.Modifier);
                else
                    RemoveSpawns(lists, file.Modifier);

                changed = true;
            }
            catch (InvalidDataException ex)
            {
                Log.Invalid(logger, file.Source, ex.Message);
            }
        }

        return changed ? new BiomeSpawns(lists.ToFrozenDictionary()) : BiomeSpawns.Vanilla;
    }

    /// <summary>
    /// The category of an entity type's mobs, by the type's identifier: a plugin type's <c>Category</c>, or vanilla's
    /// as Obsidian's spawner counts them (see <see cref="MobSpawner.SpawnCategory(EntityType?)"/>).
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

    // Like NeoForge, each spawner goes to the end of its entity type's category's list.
    private static void AddSpawns(
        Dictionary<string, IReadOnlyDictionary<string, SpawnerMob[]>> lists,
        BiomeModifierData modifier)
    {
        foreach (var biome in PluginFeatures.ResolveBiomes(modifier.Biomes))
        {
            var categories = lists.GetValueOrDefault(biome)?.ToDictionary() ?? [];

            foreach (var spawner in modifier.Spawners)
            {
                var category = CategoryOf(spawner.Type);
                categories[category] = [.. categories.GetValueOrDefault(category) ?? [], spawner];
            }

            lists[biome] = categories;
        }
    }

    // Like NeoForge, the entity types' spawners are removed from every category.
    private static void RemoveSpawns(
        Dictionary<string, IReadOnlyDictionary<string, SpawnerMob[]>> lists,
        BiomeModifierData modifier)
    {
        var removed = EntityTypeIds(modifier.EntityTypes);

        foreach (var biome in PluginFeatures.ResolveBiomes(modifier.Biomes))
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

    // A holder set of entity types to their ids: identifiers, or a single #tag.
    private static HashSet<int> EntityTypeIds(ImmutableArray<string> types)
    {
        if (types is [var only] && only.StartsWith('#'))
        {
            return TagsRegistry.TryGet("entity_type", only[1..], out var tag)
                ? [.. tag.Entries]
                : throw new InvalidDataException($"entity_types: {only} isn't an entity type tag");
        }

        return [.. types.Select(PluginTags.EntityTypeId).OfType<int>()];
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Left out {Source}: {Message}")]
        public static partial void Invalid(ILogger logger, string source, string message);
    }
}

/// <summary>
/// The spawners of each biome, by biome identifier and mob category (vanilla's names, like <c>monster</c>).
/// </summary>
internal sealed record BiomeSpawns(IReadOnlyDictionary<string, IReadOnlyDictionary<string, SpawnerMob[]>> Biomes)
{
    /// <summary>
    /// Vanilla's biomes' spawners, from Codecs/biomes.json.
    /// </summary>
    public static BiomeSpawns Vanilla { get; } = LoadVanilla();

    private static BiomeSpawns LoadVanilla()
    {
        var resource = "Obsidian.Assets.Codecs.biomes.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidDataException("Missing biome spawn data.");
        using var document = JsonDocument.Parse(stream);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // In the file's order, which MobSpawner's vanilla mob categories depend on.
        var biomes = document.RootElement
            .GetProperty("value")
            .EnumerateArray()
            .ToDictionary(
                entry => entry.GetProperty("name").GetString()!,
                entry => (IReadOnlyDictionary<string, SpawnerMob[]>)entry
                    .GetProperty("element")
                    .GetProperty("spawners")
                    .Deserialize<Dictionary<string, SpawnerMob[]>>(options)!);

        return new BiomeSpawns(biomes);
    }
}
