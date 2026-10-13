namespace Obsidian.API.World.Generator;

/// <summary>
/// A biome and the climate it's picked in, like an entry of a <c>minecraft:multi_noise</c> biome source's
/// <c>biomes</c>: where a column's climate is nearest to these ranges, the biome generates.
/// </summary>
/// <param name="Biome">The biome's identifier, vanilla's or a plugin's.</param>
/// <param name="Offset">Added to the distance to this entry, from 0 to 1, so that a higher offset is picked
/// less.</param>
public sealed record ClimateBiome(
    string Biome,
    ClimateRange Temperature,
    ClimateRange Humidity,
    ClimateRange Continentalness,
    ClimateRange Erosion,
    ClimateRange Depth,
    ClimateRange Weirdness,
    float Offset = 0);

/// <summary>
/// A range of a climate parameter, from -2 to 2. A single value converts to the range of that value alone.
/// </summary>
public readonly record struct ClimateRange(float Min, float Max)
{
    public static implicit operator ClimateRange(float value) => new(value, value);
}
