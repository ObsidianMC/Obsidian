namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Cubes an input density sample using successive multiplication.
/// </summary>
[DensityFunction("minecraft:cube")]
public sealed class CubeDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:cube";

    /// <summary>
    /// Gets the input sampled at the same position as this function.
    /// </summary>
    public required IDensityFunction Argument { get; init; }

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => Cube(this.Argument.MinValue);

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => Cube(this.Argument.MaxValue);

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => Cube(this.Argument.GetValue(x, y, z));

    /// <summary>
    /// Maps child functions and noises with the visitor, then applies it to the rebuilt function.
    /// </summary>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new CubeDensityFunction { Argument = visitor.Map(this.Argument) });

    // Math.Pow can differ from plain multiplication in the last bit.
    internal static double Cube(double value) => value * value * value;
}
