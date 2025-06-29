using System.Text.Json.Serialization;

namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeElement
{
    public required BiomeEffect Effects { get; set; }

    public required bool HasPrecipitation { get; set; }

    public float Depth { get; set; }
    public float Temperature { get; set; }
    public float Scale { get; set; }
    public float Downfall { get; set; }

    public string? Category { get; set; }
    public string? TemperatureModifier { get; set; }

    public string[][] Features { get; set; } = [];
    public Dictionary<string, string[]> Carvers { get; set; } = default!;
    public Dictionary<string, SpawnerMob[]> Spawners { get; set; } = default!;
    public Dictionary<string, SpawnCost> SpawnCosts { get; set; } = default!;

    public bool PlayerSpawnFriendly { get; set; }
}

public sealed class SpawnCost
{
    public required float Charge { get; init; }
    public required float EnergyBudget { get; init; }
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
