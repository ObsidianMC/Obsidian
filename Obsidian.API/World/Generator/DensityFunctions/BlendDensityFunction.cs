namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Blends density into pre-1.18 chunks. Without blending data this passes the argument through, as in vanilla.
/// </summary>
[DensityFunction("minecraft:blend_density")]
public sealed class BlendDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:blend_density";

    /// <summary>
    /// Gets the input sampled at the same position as this function.
    /// </summary>
    public required IDensityFunction Argument { get; init; }

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => double.NegativeInfinity;

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => double.PositiveInfinity;

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => this.Argument.GetValue(x, y, z);

    /// <summary>
    /// Maps child functions and noises with the visitor, then applies it to the rebuilt function.
    /// </summary>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new BlendDensityFunction { Argument = visitor.Map(this.Argument) });
}
