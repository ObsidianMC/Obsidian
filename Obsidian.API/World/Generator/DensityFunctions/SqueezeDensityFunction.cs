namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Clamps an input to [-1, 1] and applies the transform x/2 - x³/24.
/// </summary>
[DensityFunction("minecraft:squeeze")]
public sealed class SqueezeDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:squeeze";

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

    /// <inheritdoc/>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new SqueezeDensityFunction { Argument = visitor.Map(this.Argument) });

    internal static double Transform(double value)
    {
        var clamped = Math.Clamp(value, -1.0, 1.0);
        return clamped / 2.0 - clamped * clamped * clamped / 24.0;
    }
}
