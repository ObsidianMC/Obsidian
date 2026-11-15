using System.Text.Json.Serialization;

namespace Obsidian.API.Registry.Codecs.Biomes;

/// <summary>
/// The parts of a 26.3 biome the server uses. Vanilla's entries are generated with their climate and effects;
/// plugins' are read whole from their data files. Server rules retain the full environment map; clients receive
/// only its synced attributes.
/// </summary>
public sealed record class BiomeElement
{
    /// <summary>
    /// Gets or sets the water and vegetation colors used by this biome.
    /// </summary>
    public required BiomeEffect Effects { get; set; }

    /// <summary>
    /// Gets or sets whether the biome supports precipitation, serialized as <c>has_precipitation</c>.
    /// </summary>
    public required bool HasPrecipitation { get; set; }

    /// <summary>
    /// Gets or sets the base biome temperature, serialized as <c>temperature</c>.
    /// </summary>
    public float Temperature { get; set; }

    /// <summary>
    /// Gets or sets the biome's downfall climate value, serialized as <c>downfall</c>.
    /// </summary>
    public float Downfall { get; set; }

    /// <summary>
    /// <c>frozen</c>, or <c>none</c> or null for none.
    /// </summary>
    public string? TemperatureModifier { get; set; }

    /// <summary>
    /// The placed features of each generation step: identifiers, placed features defined inline as their JSON text
    /// (starting with <c>{</c>), or a single <c>#tag</c>. Empty for vanilla's entries, whose generation is built in.
    /// </summary>
    public ImmutableArray<ImmutableArray<string>> Features { get; set; } = [];

    /// <summary>
    /// The configured carvers: identifiers, or a single <c>#tag</c>. Empty for vanilla's entries.
    /// </summary>
    public ImmutableArray<string> Carvers { get; set; } = [];

    /// <summary>
    /// The mobs that spawn, by category like <c>monster</c>. Empty for vanilla's entries, which the mob spawner reads
    /// from vanilla's data.
    /// </summary>
    public Dictionary<string, SpawnerMob[]> Spawners { get; set; } = [];

    /// <summary>
    /// The chance of each further group of creatures spawning when a chunk generates, like vanilla's
    /// <c>gameplay/creature_world_gen_spawn_probability</c> attribute.
    /// </summary>
    public float CreatureSpawnProbability { get; set; } = 0.1f;

    /// <summary>
    /// The spawn costs by entity type identifier. Empty for vanilla's entries.
    /// </summary>
    public Dictionary<string, SpawnCost> SpawnCosts { get; set; } = [];
}

/// <summary>
/// Describes an entity type's contribution and limit in the biome's spawn potential calculation.
/// </summary>
public sealed class SpawnCost
{
    /// <summary>
    /// Gets the charge added to spawn potential when an entity of this type spawns.
    /// </summary>
    public required double Charge { get; init; }

    /// <summary>
    /// Gets the maximum permitted spawn potential energy change for this entity type.
    /// </summary>
    public required double EnergyBudget { get; init; }
}

/// <summary>
/// Describes a weighted mob entry and its spawn group size bounds.
/// </summary>
public sealed class SpawnerMob
{
    /// <summary>
    /// Gets the namespaced entity type identifier to spawn.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// The 26.3 pack-size provider. The bounds below also support Obsidian's biome modifier API.
    /// </summary>
    [JsonIgnore]
    public IIntProvider? Count { get; init; }

    /// <summary>
    /// Gets the upper bound of the 26.3 <c>count</c> provider; biome-modifier compatibility data may supply
    /// <c>maxCount</c> instead.
    /// </summary>
    [JsonPropertyName("maxCount")]
    public required int MaxCount { get; init; }

    /// <summary>
    /// Gets the lower bound of the 26.3 <c>count</c> provider; biome-modifier compatibility data may supply
    /// <c>minCount</c> instead.
    /// </summary>
    [JsonPropertyName("minCount")]
    public required int MinCount { get; init; }

    /// <summary>
    /// Gets the entry's relative selection weight, read from <c>weight</c>.
    /// </summary>
    [JsonPropertyName("weight")]
    public required int Weight { get; init; }
}
