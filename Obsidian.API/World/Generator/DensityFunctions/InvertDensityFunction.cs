namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Returns the reciprocal of an input density sample.
/// </summary>
[DensityFunction("minecraft:invert")]
public sealed class InvertDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:invert";

    /// <summary>
    /// Gets the input sampled at the same position as this function.
    /// </summary>
    public required IDensityFunction Argument { get; init; }

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => this.Bounds.Min;

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => this.Bounds.Max;

    // An input range spanning zero makes the output unbounded.
    private DensityBounds Bounds => field ??= this.Argument.MinValue < 0.0 && this.Argument.MaxValue > 0.0
        ? new(double.NegativeInfinity, double.PositiveInfinity)
        : new(1.0 / this.Argument.MaxValue, 1.0 / this.Argument.MinValue);

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => 1.0 / this.Argument.GetValue(x, y, z);

    /// <summary>
    /// Maps child functions and noises with the visitor, then applies it to the rebuilt function.
    /// </summary>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new InvertDensityFunction { Argument = visitor.Map(this.Argument) });
}
