using System.Text.Json.Serialization;

namespace Obsidian.API.Registry.Codecs.Biomes;

/// <summary>
/// The parts of a 1.21.11 biome the server uses. Vanilla's entries are generated with their climate and effects;
/// plugins' are read whole from their data files. A biome's environment attributes are only sent to clients.
/// </summary>
public sealed record class BiomeElement
{
    public required BiomeEffect Effects { get; set; }

    public required bool HasPrecipitation { get; set; }

    public float Temperature { get; set; }

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
    /// The spawn costs by entity type identifier. Empty for vanilla's entries.
    /// </summary>
    public Dictionary<string, SpawnCost> SpawnCosts { get; set; } = [];
}

public sealed class SpawnCost
{
    public required double Charge { get; init; }

    public required double EnergyBudget { get; init; }
}

public sealed class SpawnerMob
{
    public required string Type { get; init; }

    [JsonPropertyName("maxCount")]
    public required int MaxCount { get; init; }

    [JsonPropertyName("minCount")]
    public required int MinCount { get; init; }

    [JsonPropertyName("weight")]
    public required int Weight { get; init; }
}
