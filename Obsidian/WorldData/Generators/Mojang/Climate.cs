namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Climate values are compared as fixed-point longs (value * 10000), like vanilla.
/// </summary>
internal static class Climate
{
    /// <summary>
    /// Converts a climate value to fixed point. The float multiply and truncation match vanilla.
    /// </summary>
    public static long Quantize(float value) => (long)(value * 10000.0f);
}

/// <summary>
/// An inclusive range of quantized climate values.
/// </summary>
internal readonly record struct ClimateParameter(long Min, long Max)
{
    /// <summary>
    /// Distance from <paramref name="value"/> to the range; 0 when inside.
    /// </summary>
    public long Distance(long value)
    {
        var above = value - this.Max;
        var below = this.Min - value;
        return above > 0L ? above : Math.Max(below, 0L);
    }

    public ClimateParameter Span(ClimateParameter other) => new(Math.Min(this.Min, other.Min), Math.Max(this.Max, other.Max));

    public long Midpoint => (this.Min + this.Max) / 2L;
}

/// <summary>
/// Quantized climate sampled at a position.
/// </summary>
internal readonly record struct TargetPoint(long Temperature, long Humidity, long Continentalness, long Erosion, long Depth, long Weirdness)
{
    /// <summary>
    /// The point in the 7D search space; the last axis is the biome offset, which targets always have at 0.
    /// </summary>
    public long[] ToParameterArray() => [this.Temperature, this.Humidity, this.Continentalness, this.Erosion, this.Depth, this.Weirdness, 0L];
}

/// <summary>
/// Climate ranges a biome occupies, plus an offset that makes it less likely to be chosen.
/// </summary>
internal sealed record ParameterPoint(
    ClimateParameter Temperature,
    ClimateParameter Humidity,
    ClimateParameter Continentalness,
    ClimateParameter Erosion,
    ClimateParameter Depth,
    ClimateParameter Weirdness,
    long Offset)
{
    /// <summary>
    /// Squared distance from a sampled climate to these ranges, used to rank spawn positions.
    /// </summary>
    public long Fitness(TargetPoint point) =>
        Square(this.Temperature.Distance(point.Temperature))
        + Square(this.Humidity.Distance(point.Humidity))
        + Square(this.Continentalness.Distance(point.Continentalness))
        + Square(this.Erosion.Distance(point.Erosion))
        + Square(this.Depth.Distance(point.Depth))
        + Square(this.Weirdness.Distance(point.Weirdness))
        + Square(this.Offset);

    public ClimateParameter[] ParameterSpace() =>
        [this.Temperature, this.Humidity, this.Continentalness, this.Erosion, this.Depth, this.Weirdness, new(this.Offset, this.Offset)];

    private static long Square(long value) => value * value;
}
