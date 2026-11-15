namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Samples the noise at (z / 4, x / 4, 0), scaled by 4. Used for z offsets.
/// </summary>
[DensityFunction("minecraft:shift_b")]
public sealed class ShiftBDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:shift_b";

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
    public double GetValue(double x, double y, double z) => this.Argument.GetValue(z * 0.25, x * 0.25, 0.0) * 4.0;

    /// <summary>
    /// Maps child functions and noises with the visitor, then applies it to the rebuilt function.
    /// </summary>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new ShiftBDensityFunction { Argument = visitor.VisitNoise(this.Argument) });
}
