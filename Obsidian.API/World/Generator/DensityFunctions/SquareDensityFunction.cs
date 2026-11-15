namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Squares an input density sample.
/// </summary>
[DensityFunction("minecraft:square")]
public sealed class SquareDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:square";

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
        Math.Max(Square(this.Argument.MinValue), Square(this.Argument.MaxValue)));

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => Square(this.Argument.GetValue(x, y, z));

    /// <summary>
    /// Maps child functions and noises with the visitor, then applies it to the rebuilt function.
    /// </summary>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new SquareDensityFunction { Argument = visitor.Map(this.Argument) });

    internal static double Square(double value) => value * value;
}
