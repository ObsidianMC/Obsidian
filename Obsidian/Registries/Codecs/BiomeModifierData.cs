using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Structures;
using System.Text.Json;
using static Obsidian.Registries.Codecs.CodecJson;

namespace Obsidian.Registries.Codecs;

/// <summary>
/// A NeoForge biome modifier from a plugin's <c>data/&lt;namespace&gt;/neoforge/biome_modifier</c> folder, in
/// NeoForge 1.21.11's format. Every type has <c>biomes</c>:
/// <list type="bullet">
/// <item><c>neoforge:add_features</c> (<c>features</c>, <c>step</c>) and <c>neoforge:remove_features</c>
/// (<c>features</c>, optional <c>steps</c>);</item>
/// <item><c>neoforge:add_spawns</c> (<c>spawners</c>: one or a list of <c>{type, weight, minCount, maxCount}</c>) and
/// <c>neoforge:remove_spawns</c> (<c>entity_types</c>);</item>
/// <item><c>neoforge:add_carvers</c> and <c>neoforge:remove_carvers</c> (<c>carvers</c>);</item>
/// <item><c>neoforge:add_spawn_costs</c> (<c>entity_types</c>, <c>spawn_cost</c>) and
/// <c>neoforge:remove_spawn_costs</c> (<c>entity_types</c>), which are read but change nothing: Obsidian's mob
/// spawner doesn't use spawn costs, vanilla's included;</item>
/// <item><c>neoforge:none</c>.</item>
/// </list>
/// </summary>
/// <remarks>
/// <c>biomes</c>, <c>features</c>, <c>carvers</c> and <c>entity_types</c> are holder sets: an identifier, a list of
/// them or a <c>#tag</c>. Features added can also be defined inline. References and tags are checked when the
/// modifier is applied (see <see cref="PluginFeatures"/> and <see cref="PluginSpawns"/>), since they may be other
/// plugins' entries, except entity types, which are registered by then.
/// </remarks>
/// <param name="Type">The modifier type, like <c>neoforge:add_features</c>.</param>
/// <param name="Biomes">Biome identifiers, or a single <c>#tag</c>.</param>
/// <param name="Features">Placed features: identifiers or a single <c>#tag</c> as JSON strings, or objects defined
/// inline.</param>
/// <param name="Steps">The decoration steps added to or removed from.</param>
internal sealed record BiomeModifierData(
    string Type,
    ImmutableArray<string> Biomes,
    ImmutableArray<JsonElement> Features,
    ImmutableArray<DecorationStep> Steps)
{
    public const string AddFeatures = "neoforge:add_features";

    public const string RemoveFeatures = "neoforge:remove_features";

    public const string AddSpawns = "neoforge:add_spawns";

    public const string RemoveSpawns = "neoforge:remove_spawns";

    public const string AddCarvers = "neoforge:add_carvers";

    public const string RemoveCarvers = "neoforge:remove_carvers";

    public const string AddSpawnCosts = "neoforge:add_spawn_costs";

    public const string RemoveSpawnCosts = "neoforge:remove_spawn_costs";

    public const string None = "neoforge:none";

    // Vanilla's GenerationStep.Decoration names, in DecorationStep's order.
    private static readonly string[] stepNames = System.Enum.GetNames<DecorationStep>()
        .Select(name => name.ToSnakeCase())
        .ToArray();

    /// <summary>
    /// Configured carvers, for carver modifiers: identifiers, or a single <c>#tag</c>.
    /// </summary>
    public ImmutableArray<string> Carvers { get; init; } = [];

    /// <summary>
    /// The spawners an <c>add_spawns</c> modifier adds.
    /// </summary>
    public ImmutableArray<SpawnerMob> Spawners { get; init; } = [];

    /// <summary>
    /// Entity types, for spawn and spawn cost removals: identifiers, or a single <c>#tag</c>.
    /// </summary>
    public ImmutableArray<string> EntityTypes { get; init; } = [];

    /// <summary>
    /// Whether the modifier runs in NeoForge's add phase, before every modifier of the remove phase.
    /// </summary>
    public bool Adds => this.Type is AddFeatures or AddSpawns or AddCarvers or AddSpawnCosts;

    public static BiomeModifierData Read(JsonElement root)
    {
        Object(root, "");

        var type = Identifier(Required(root, "", "type"), "type");

        if (type == None)
            return new BiomeModifierData(type, [], [], []);

        var biomes = ReadHolderSet(Required(root, "", "biomes"), "biomes");

        switch (type)
        {
            case AddFeatures:
                return new BiomeModifierData(
                    type,
                    biomes,
                    ReadFeatures(Required(root, "", "features"), "features", inline: true),
                    [ReadStep(Required(root, "", "step"), "step")]);
            case RemoveFeatures:
                var steps = TryGet(root, "steps", out var value)
                    ? ReadSteps(value, "steps")
                    : [.. System.Enum.GetValues<DecorationStep>()];

                return new BiomeModifierData(
                    type,
                    biomes,
                    ReadFeatures(Required(root, "", "features"), "features", inline: false),
                    steps);
            case AddSpawns:
                return new BiomeModifierData(type, biomes, [], [])
                {
                    Spawners = ReadSpawners(Required(root, "", "spawners"), "spawners")
                };
            case RemoveSpawns or RemoveSpawnCosts:
                return new BiomeModifierData(type, biomes, [], [])
                {
                    EntityTypes = ReadEntityTypes(Required(root, "", "entity_types"), "entity_types")
                };
            case AddSpawnCosts:
                var cost = Object(Required(root, "", "spawn_cost"), "spawn_cost");
                Double(Required(cost, "spawn_cost", "energy_budget"), "spawn_cost.energy_budget");
                Double(Required(cost, "spawn_cost", "charge"), "spawn_cost.charge");

                return new BiomeModifierData(type, biomes, [], [])
                {
                    EntityTypes = ReadEntityTypes(Required(root, "", "entity_types"), "entity_types")
                };
            case AddCarvers or RemoveCarvers:
                // NeoForge 1.21.11's carver modifiers have no steps: carvers no longer run per carving step.
                return new BiomeModifierData(type, biomes, [], [])
                {
                    Carvers = ReadHolderSet(Required(root, "", "carvers"), "carvers")
                };
            default:
                throw Error("type", $"{type} isn't a NeoForge biome modifier type plugins can use");
        }
    }

    // A holder set like vanilla's RegistryCodecs.homogeneousList, by reference: a #tag, an identifier or a list of
    // them. Other holder set types (NeoForge's neoforge:any, neoforge:and, ...) aren't supported.
    private static ImmutableArray<string> ReadHolderSet(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.String && value.GetString()!.StartsWith('#'))
            return [$"#{Tag(value, path)}"];

        if (value.ValueKind == JsonValueKind.Object)
            throw Error(path, "expected an identifier, a list of them or a #tag; other holder sets aren't supported");

        if (value.ValueKind != JsonValueKind.Array)
            return [Identifier(value, path)];

        return [.. value.EnumerateArray().Select((entry, i) => Identifier(entry, Index(path, i)))];
    }

    // Entity types, which are registered before plugins' files are read, so they're checked here; tags aren't.
    private static ImmutableArray<string> ReadEntityTypes(JsonElement value, string path)
    {
        var types = ReadHolderSet(value, path);

        for (var i = 0; i < types.Length; i++)
        {
            if (!types[i].StartsWith('#') && !RegistryIds.IsEntityType(types[i]))
                throw Error(value.ValueKind == JsonValueKind.Array ? Index(path, i) : path, "isn't an entity type");
        }

        return types;
    }

    // Like NeoForge, one spawner or a list of them.
    private static ImmutableArray<SpawnerMob> ReadSpawners(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return [BiomeData.ReadSpawner(value, path)];

        return [.. value.EnumerateArray().Select((spawner, i) => BiomeData.ReadSpawner(spawner, Index(path, i)))];
    }

    // Placed features as JSON: a #tag or identifiers (normalised), or, where allowed, inline objects.
    private static ImmutableArray<JsonElement> ReadFeatures(JsonElement value, string path, bool inline)
    {
        if (value.ValueKind == JsonValueKind.String && value.GetString()!.StartsWith('#'))
            return [JsonSerializer.SerializeToElement($"#{Tag(value, path)}")];

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

        return JsonSerializer.SerializeToElement(Identifier(value, path));
    }

    private static ImmutableArray<DecorationStep> ReadSteps(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return [ReadStep(value, path)];

        return [.. value.EnumerateArray().Select((step, i) => ReadStep(step, Index(path, i)))];
    }

    private static DecorationStep ReadStep(JsonElement value, string path) =>
        (DecorationStep)System.Array.IndexOf(stepNames, Enum(value, path, stepNames));
}
