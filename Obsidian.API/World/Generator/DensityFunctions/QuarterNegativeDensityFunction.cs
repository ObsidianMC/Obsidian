namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Quarters negative density samples and leaves nonnegative samples unchanged.
/// </summary>
[DensityFunction("minecraft:quarter_negative")]
public sealed class QuarterNegativeDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:quarter_negative";

    /// <summary>
    /// Gets the input sampled at the same position as this function.
    /// </summary>
    public required IDensityFunction Argument { get; init; }

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => Transform(this.Argument.MinValue);

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => Transform(this.Argument.MaxValue);

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => Transform(this.Argument.GetValue(x, y, z));

    /// <summary>
    /// Maps child functions and noises with the visitor, then applies it to the rebuilt function.
    /// </summary>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new QuarterNegativeDensityFunction { Argument = visitor.Map(this.Argument) });

    internal static double Transform(double value) => value > 0.0 ? value : value * 0.25;
}
