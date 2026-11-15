namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Evaluates a terrain spline at the sampled position.
/// </summary>
[DensityFunction("minecraft:spline")]
public sealed class SplineDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:spline";

    /// <summary>
    /// The spline: a multipoint <see cref="World.Spline"/>, or a <see cref="SplineConstant"/>, as vanilla's codec
    /// takes a number for a constant one.
    /// </summary>
    public required ISpline Spline { get; init; }

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => this.Spline.MinValue;

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => this.Spline.MaxValue;

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => this.Spline.Apply(x, y, z);

    /// <inheritdoc/>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new SplineDensityFunction { Spline = this.Spline.MapAll(visitor) });
}
