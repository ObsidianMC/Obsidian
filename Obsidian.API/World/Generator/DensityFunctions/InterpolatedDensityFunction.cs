namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Samples the argument at cell corners and trilinearly interpolates between them during terrain fill.
/// </summary>
/// <remarks>
/// Only a marker: evaluating it directly returns the argument. Chunk generation swaps it for a
/// chunk-bound implementation through <see cref="IDensityFunction.MapAll"/>.
/// </remarks>
[DensityFunction("minecraft:interpolated")]
public sealed class InterpolatedDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:interpolated";

    /// <summary>
    /// Gets the input sampled at the same position as this function.
    /// </summary>
    public required IDensityFunction Argument { get; init; }

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => this.Argument.MinValue;

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => this.Argument.MaxValue;

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => this.Argument.GetValue(x, y, z);

    /// <inheritdoc/>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new InterpolatedDensityFunction { Argument = visitor.Map(this.Argument) });
}
