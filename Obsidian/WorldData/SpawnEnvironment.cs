using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Nbt;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData.Generators.Density;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Obsidian.WorldData;

/// <summary>Natural spawning's resolved environment, including explicitly empty category overrides.</summary>
internal sealed record SpawnSettings(
    IReadOnlyDictionary<string, SpawnerMob[]> Categories,
    IReadOnlyDictionary<string, SpawnCost> Costs,
    float CreatureProbability)
{
    public static SpawnSettings Empty { get; } = new(
        ImmutableDictionary<string, SpawnerMob[]>.Empty,
        ImmutableDictionary<string, SpawnCost>.Empty,
        BiomeSpawns.DefaultCreatureProbability);
}

/// <summary>
/// Resolves 26.3's spawn attributes from default, dimension and biome maps. Overlay replaces whole categories
/// (including empty lists), merges costs by entity type, and retains unspecified values.
/// </summary>
internal static class SpawnEnvironment
{
    internal const string SpawnsAttribute = "minecraft:gameplay/natural_mob_spawns";
    internal const string ProbabilityAttribute = "minecraft:gameplay/creature_world_gen_spawn_probability";

    private static readonly ConcurrentDictionary<(string Registry, string Id), EnvironmentAttributeMap> vanilla = new();
    private static readonly ConditionalWeakTable<INbtTag, SpawnSettings> decoded = new();

    public static SpawnSettings Resolve(DimensionCodec dimension, BiomeCodec biome)
    {
        var dimensionMap = LevelEnvironment.AttributesOf(dimension.Element)
            ?? VanillaMap("dimension_type", dimension.Name);
        var biomeMap = LevelEnvironment.AttributesOf(biome.Element) ?? VanillaMap("biome", biome.Name);

        return Resolve(dimensionMap, biomeMap);
    }

    internal static SpawnSettings Resolve(EnvironmentAttributeMap dimension, EnvironmentAttributeMap biome)
    {
        var settings = Apply(dimension, SpawnSettings.Empty);
        settings = Apply(biome, settings);
        var probability = dimension.Apply(ProbabilityAttribute, SpawnSettings.Empty.CreatureProbability);
        probability = biome.Apply(ProbabilityAttribute, probability);

        return settings with { CreatureProbability = Math.Clamp(probability, 0f, 0.9999999f) };
    }

    private static EnvironmentAttributeMap VanillaMap(string registry, string id)
    {
        // Custom entries with no attached map inherit the defaults.
        if (!id.StartsWith("minecraft:", StringComparison.Ordinal))
        {
            return EnvironmentAttributeMap.Empty;
        }

        return vanilla.GetOrAdd((registry, id), key =>
        {
            var data = WorldgenData.Vanilla.Read(key.Registry, key.Id);
            return data.TryGetProperty("attributes", out var attributes)
                ? EnvironmentAttributes.Read(attributes, "attributes", onlyPositional: key.Registry == "biome")
                : EnvironmentAttributeMap.Empty;
        });
    }

    // Keep providers beside their encoded attribute values: natural spawning samples their full distributions.
    internal static void Remember(NbtCompound value, Dictionary<string, SpawnerMob[]> categories,
        Dictionary<string, SpawnCost> costs) =>
        decoded.Add(value, new SpawnSettings(categories, costs, SpawnSettings.Empty.CreatureProbability));

    private static SpawnSettings Apply(EnvironmentAttributeMap map, SpawnSettings previous)
    {
        if (map.Find(SpawnsAttribute) is not NbtCompound value)
        {
            return previous;
        }

        var overlay = value.TryGetTag<NbtTag<string>>("modifier", out var modifier)
            && modifier.Value == "overlay";

        if (overlay)
        {
            value = (NbtCompound)value["argument"];
        }

        var settings = decoded.GetValue(value, _ =>
            throw new InvalidOperationException("Spawn attributes must be read through their codec."));

        if (!overlay)
        {
            return settings;
        }

        var categories = previous.Categories.ToDictionary();
        var costs = previous.Costs.ToDictionary();

        foreach (var (category, spawners) in settings.Categories)
        {
            categories[category] = spawners;
        }

        foreach (var (type, cost) in settings.Costs)
        {
            costs[type] = cost;
        }

        return new SpawnSettings(categories, costs, previous.CreatureProbability);
    }
}
