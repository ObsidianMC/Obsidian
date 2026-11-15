namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Samples the noise at a quarter of the block position, scaled by 4.
/// </summary>
[DensityFunction("minecraft:shift")]
public sealed class ShiftDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:shift";

    /// <summary>
    /// Gets the noise used to produce the coordinate offset.
    /// </summary>
    public required INoise Argument { get; init; }

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => -this.MaxValue;

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => this.Argument.MaxValue * 4.0;

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => this.Argument.GetValue(x * 0.25, y * 0.25, z * 0.25) * 4.0;

    /// <inheritdoc/>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new ShiftDensityFunction { Argument = visitor.VisitNoise(this.Argument) });
}
