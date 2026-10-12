using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Structures;
using System.Text.Json;
using static Obsidian.Registries.Codecs.CodecJson;

namespace Obsidian.Registries.Codecs;

/// <summary>
/// A biome modifier from a plugin's <c>data/&lt;namespace&gt;/biome_modifier</c> folder, Obsidian's own format: the
/// <c>biomes</c> it changes (less those it <c>exclude</c>s), what it adds to them and what it removes from them.
/// </summary>
/// <remarks>
/// <code>
/// {
///   "biomes": ["#c:is_overworld", "ruby:crystal_plains"],
///   "exclude": "#c:is_mushroom",
///   "add": {
///     "features": { "underground_ores": ["ruby:ore_ruby"] },
///     "spawners": [{ "type": "ruby:golem", "weight": 5, "minCount": 1, "maxCount": 2 }],
///     "carvers": "ruby:crystal_caves",
///     "spawn_costs": { "ruby:golem": { "energy_budget": 0.12, "charge": 1.0 } }
///   },
///   "remove": { "features": "minecraft:ore_coal_upper", "spawners": "minecraft:creeper" }
/// }
/// </code>
/// Biomes, features, carvers and entity types are sets: an identifier, a <c>#tag</c>, or a list mixing both. Features
/// added may also be defined inline. References and tags are checked when the modifier is applied (see
/// <see cref="PluginFeatures"/> and <see cref="PluginSpawns"/>), since they may be other plugins' entries, except
/// entity types, which are registered by then. Unknown fields are errors, so typos don't go unnoticed.
/// </remarks>
/// <param name="Biomes">The biomes changed: identifiers and <c>#tag</c>s.</param>
/// <param name="Exclude">Biomes left unchanged even when in <paramref name="Biomes"/>.</param>
/// <param name="Add">What the modifier adds.</param>
/// <param name="Remove">What the modifier removes.</param>
internal sealed record BiomeModifierData(
    ImmutableArray<string> Biomes,
    ImmutableArray<string> Exclude,
    BiomeAdditions Add,
    BiomeRemovals Remove)
{
    // Vanilla's GenerationStep.Decoration names, in DecorationStep's order.
    private static readonly string[] stepNames = System.Enum.GetNames<DecorationStep>()
        .Select(name => name.ToSnakeCase())
        .ToArray();

    public static BiomeModifierData Read(JsonElement root)
    {
        Fields(root, "", "biomes", "exclude", "add", "remove");

        var exclude = TryGet(root, "exclude", out var excluded)
            ? ReadSet(excluded, "exclude")
            : [];

        return new BiomeModifierData(
            ReadSet(Required(root, "", "biomes"), "biomes"),
            exclude,
            TryGet(root, "add", out var add) ? ReadAdditions(add, "add") : BiomeAdditions.None,
            TryGet(root, "remove", out var remove) ? ReadRemovals(remove, "remove") : BiomeRemovals.None);
    }

    private static BiomeAdditions ReadAdditions(JsonElement value, string path)
    {
        Fields(value, path, "features", "spawners", "carvers", "spawn_costs");

        var additions = BiomeAdditions.None;

        if (TryGet(value, "features", out var features))
            additions = additions with { Features = ReadSteps(features, Field(path, "features"), inline: true) };

        if (TryGet(value, "spawners", out var spawners))
            additions = additions with { Spawners = ReadSpawners(spawners, Field(path, "spawners")) };

        if (TryGet(value, "carvers", out var carvers))
            additions = additions with { Carvers = ReadSet(carvers, Field(path, "carvers")) };

        if (TryGet(value, "spawn_costs", out var costs))
        {
            additions = additions with
            {
                SpawnCosts = BiomeData.ReadSpawnCosts(costs, Field(path, "spawn_costs")).ToImmutableDictionary()
            };
        }

        return additions;
    }

    private static BiomeRemovals ReadRemovals(JsonElement value, string path)
    {
        Fields(value, path, "features", "spawners", "carvers", "spawn_costs");

        var removals = BiomeRemovals.None;

        if (TryGet(value, "features", out var features))
        {
            // A plain set removes the features from every step.
            var steps = features.ValueKind == JsonValueKind.Object
                ? ReadSteps(features, Field(path, "features"), inline: false)
                : [.. System.Enum.GetValues<DecorationStep>()
                    .Select(step => new ModifierFeatures(step, ReadFeatures(features, Field(path, "features"), false)))];

            removals = removals with { Features = steps };
        }

        if (TryGet(value, "spawners", out var spawners))
            removals = removals with { Spawners = ReadEntityTypes(spawners, Field(path, "spawners")) };

        if (TryGet(value, "carvers", out var carvers))
            removals = removals with { Carvers = ReadSet(carvers, Field(path, "carvers")) };

        if (TryGet(value, "spawn_costs", out var costs))
            removals = removals with { SpawnCosts = ReadEntityTypes(costs, Field(path, "spawn_costs")) };

        return removals;
    }

    // Rejects fields the format doesn't have.
    private static void Fields(JsonElement value, string path, params ReadOnlySpan<string> names)
    {
        Object(value, path);

        foreach (var property in value.EnumerateObject())
        {
            if (!names.Contains(property.Name))
                throw Error(Field(path, property.Name), "isn't a field of a biome modifier");
        }
    }

    // A set: an identifier, a #tag or a list mixing both; tags are kept with their #.
    private static ImmutableArray<string> ReadSet(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return [ReadSetEntry(value, path)];

        return [.. value.EnumerateArray().Select((entry, i) => ReadSetEntry(entry, Index(path, i)))];
    }

    private static string ReadSetEntry(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.String && value.GetString()!.StartsWith('#')
            ? $"#{Tag(value, path)}"
            : Identifier(value, path);

    // Entity types, which are registered before plugins' files are read, so they're checked here; tags aren't.
    private static ImmutableArray<string> ReadEntityTypes(JsonElement value, string path)
    {
        var types = ReadSet(value, path);

        for (var i = 0; i < types.Length; i++)
        {
            if (!types[i].StartsWith('#') && !RegistryIds.IsEntityType(types[i]))
                throw Error(value.ValueKind == JsonValueKind.Array ? Index(path, i) : path, "isn't an entity type");
        }

        return types;
    }

    // One spawner or a list of them.
    private static ImmutableArray<SpawnerMob> ReadSpawners(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return [BiomeData.ReadSpawner(value, path)];

        return [.. value.EnumerateArray().Select((spawner, i) => BiomeData.ReadSpawner(spawner, Index(path, i)))];
    }

    // Features by decoration step: { "<step>": features, ... }.
    private static ImmutableArray<ModifierFeatures> ReadSteps(JsonElement value, string path, bool inline)
    {
        Object(value, path);

        var steps = ImmutableArray.CreateBuilder<ModifierFeatures>();

        foreach (var property in value.EnumerateObject())
        {
            var stepPath = Field(path, property.Name);
            var step = System.Array.IndexOf(stepNames, property.Name);

            if (step < 0)
                throw Error(stepPath, $"isn't a decoration step: expected one of {string.Join(", ", stepNames)}");

            steps.Add(new ModifierFeatures((DecorationStep)step, ReadFeatures(property.Value, stepPath, inline)));
        }

        return steps.ToImmutable();
    }

    // Placed features as JSON: identifiers and #tags (normalised), or, where allowed, inline objects.
    private static ImmutableArray<JsonElement> ReadFeatures(JsonElement value, string path, bool inline)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return [ReadFeature(value, path, inline)];

        return [.. value.EnumerateArray().Select((feature, i) => ReadFeature(feature, Index(path, i), inline))];
    }

    private static JsonElement ReadFeature(JsonElement value, string path, bool inline)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            return inline
                ? value.Clone()
                : throw Error(path, "a feature defined inline is in no biome; refer to one by its identifier");
        }

        return JsonSerializer.SerializeToElement(ReadSetEntry(value, path));
    }
}

/// <summary>
/// Placed features of one decoration step, as JSON: identifier and <c>#tag</c> strings, or objects defined inline.
/// </summary>
internal sealed record ModifierFeatures(DecorationStep Step, ImmutableArray<JsonElement> Features);

/// <summary>
/// What a biome modifier adds to its biomes: features at the end of their steps, spawners at the end of their entity
/// type's category, carvers at the end of the biomes' carvers, and spawn costs, replacing a type's cost.
/// </summary>
internal sealed record BiomeAdditions
{
    public static BiomeAdditions None { get; } = new();

    public ImmutableArray<ModifierFeatures> Features { get; init; } = [];

    public ImmutableArray<SpawnerMob> Spawners { get; init; } = [];

    /// <summary>
    /// Configured carvers: identifiers and <c>#tag</c>s.
    /// </summary>
    public ImmutableArray<string> Carvers { get; init; } = [];

    /// <summary>
    /// Spawn costs by entity type identifier.
    /// </summary>
    public ImmutableDictionary<string, SpawnCost> SpawnCosts { get; init; } =
        ImmutableDictionary<string, SpawnCost>.Empty;
}

/// <summary>
/// What a biome modifier removes from its biomes: features from the given steps, the spawners and spawn costs of
/// entity types, and carvers.
/// </summary>
internal sealed record BiomeRemovals
{
    public static BiomeRemovals None { get; } = new();

    public ImmutableArray<ModifierFeatures> Features { get; init; } = [];

    /// <summary>
    /// The entity types whose spawners are removed, from every category: identifiers and <c>#tag</c>s.
    /// </summary>
    public ImmutableArray<string> Spawners { get; init; } = [];

    /// <summary>
    /// Configured carvers: identifiers and <c>#tag</c>s.
    /// </summary>
    public ImmutableArray<string> Carvers { get; init; } = [];

    /// <summary>
    /// The entity types whose spawn costs are removed: identifiers and <c>#tag</c>s.
    /// </summary>
    public ImmutableArray<string> SpawnCosts { get; init; } = [];
}
