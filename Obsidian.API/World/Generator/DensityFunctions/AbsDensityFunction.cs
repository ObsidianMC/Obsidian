namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Returns the absolute value of an input density function.
/// </summary>
[DensityFunction("minecraft:abs")]
public sealed class AbsDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:abs";

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

    private DensityBounds Bounds => field ??= new(Math.Max(0.0, this.Argument.MinValue),
        Math.Max(Math.Abs(this.Argument.MinValue), Math.Abs(this.Argument.MaxValue)));

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => Math.Abs(this.Argument.GetValue(x, y, z));

    /// <inheritdoc/>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new AbsDensityFunction { Argument = visitor.Map(this.Argument) });
}
