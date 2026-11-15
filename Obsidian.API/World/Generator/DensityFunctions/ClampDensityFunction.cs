namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Limits an input density sample to a configured interval.
/// </summary>
[DensityFunction("minecraft:clamp")]
public sealed class ClampDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:clamp";

    /// <summary>
    /// Gets the density function to clamp.
    /// </summary>
    public required IDensityFunction Input { get; init; }

    /// <summary>
    /// Gets the inclusive lower limit, read from <c>min</c>.
    /// </summary>
    public required double Min { get; init; }
    /// <summary>
    /// Gets the inclusive upper limit, read from <c>max</c>.
    /// </summary>
    public required double Max { get; init; }

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => this.Min;

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => this.Max;

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => Math.Clamp(this.Input.GetValue(x, y, z), this.Min, this.Max);

    /// <inheritdoc/>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new ClampDensityFunction { Input = visitor.Map(this.Input), Min = this.Min, Max = this.Max });
}
