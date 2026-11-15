namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Samples the noise at (x / 4, 0, z / 4), scaled by 4. Used for x offsets.
/// </summary>
[DensityFunction("minecraft:shift_a")]
public sealed class ShiftADensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:shift_a";

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
    public double GetValue(double x, double y, double z) => this.Argument.GetValue(x * 0.25, 0.0, z * 0.25) * 4.0;

    /// <inheritdoc/>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new ShiftADensityFunction { Argument = visitor.VisitNoise(this.Argument) });
}
