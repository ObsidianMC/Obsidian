using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>26.3's resolved datagen lists, retaining report order and quantized range endpoints.</summary>
internal sealed class BiomeParameters
{
    private BiomeParameters(IReadOnlyList<(ParameterPoint Point, string Biome)> entries)
    {
        this.Entries = entries;
        this.Tree = new BiomeParameterTree<string>(entries, childrenPerNode: 19);
    }

    public IReadOnlyList<(ParameterPoint Point, string Biome)> Entries { get; }

    // 26.3 changed the branching factor from 6 to 19. It affects equal-distance winners.
    public BiomeParameterTree<string> Tree { get; }

    public static BiomeParameters Read(WorldgenData data, string preset)
    {
        var report = data.Read("biome_parameters", preset);
        var entries = new List<(ParameterPoint, string)>();

        foreach (var entry in report.GetProperty("biomes").EnumerateArray())
        {
            entries.Add((ReadPoint(entry.GetProperty("parameters")), entry.GetProperty("biome").GetString()!));
        }

        return new BiomeParameters(entries);
    }

    internal static ParameterPoint ReadPoint(JsonElement parameters) => new(
        ReadRange(parameters.GetProperty("temperature")),
        ReadRange(parameters.GetProperty("humidity")),
        ReadRange(parameters.GetProperty("continentalness")),
        ReadRange(parameters.GetProperty("erosion")),
        ReadRange(parameters.GetProperty("depth")),
        ReadRange(parameters.GetProperty("weirdness")),
        Requantize(parameters.GetProperty("offset")));

    private static ClimateParameter ReadRange(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            return new ClimateParameter(Requantize(value[0]), Requantize(value[1]));
        }

        var coordinate = Requantize(value);
        return new ClimateParameter(coordinate, coordinate);
    }

    // Reports unquantize existing integer endpoints. Repeating float truncation would lose endpoints such as 0.11.
    private static long Requantize(JsonElement value) => (long)Math.Round(value.GetDouble() * 10000.0);
}
