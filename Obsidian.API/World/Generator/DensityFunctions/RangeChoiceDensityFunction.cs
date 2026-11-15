namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Selects one of two density functions using a half-open interval of the control value.
/// </summary>
[DensityFunction("minecraft:range_choice")]
public sealed class RangeChoiceDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:range_choice";

    /// <summary>
    /// Gets the control function tested against the selection interval.
    /// </summary>
    public required IDensityFunction Input { get; init; }

    /// <summary>
    /// Gets the function sampled when the control value is within the interval.
    /// </summary>
    public required IDensityFunction WhenInRange { get; init; }

    /// <summary>
    /// Gets the function sampled when the control value is outside the interval.
    /// </summary>
    public required IDensityFunction WhenOutOfRange { get; init; }

    /// <summary>
    /// Gets the inclusive lower selection threshold, read from <c>min_inclusive</c>.
    /// </summary>
    public required double MinInclusive { get; init; }

    /// <summary>
    /// Gets the exclusive upper selection threshold, read from <c>max_exclusive</c>.
    /// </summary>
    public required double MaxExclusive { get; init; }

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => Math.Min(this.WhenInRange.MinValue, this.WhenOutOfRange.MinValue);

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => Math.Max(this.WhenInRange.MaxValue, this.WhenOutOfRange.MaxValue);

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z)
    {
        var control = this.Input.GetValue(x, y, z);
        if (control >= this.MinInclusive && control < this.MaxExclusive)
            return this.WhenInRange.GetValue(x, y, z);

        return this.WhenOutOfRange.GetValue(x, y, z);
    }

    /// <inheritdoc/>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new RangeChoiceDensityFunction
    {
        Input = visitor.Map(this.Input),
        WhenInRange = visitor.Map(this.WhenInRange),
        WhenOutOfRange = visitor.Map(this.WhenOutOfRange),
        MinInclusive = this.MinInclusive,
        MaxExclusive = this.MaxExclusive
    });
}
