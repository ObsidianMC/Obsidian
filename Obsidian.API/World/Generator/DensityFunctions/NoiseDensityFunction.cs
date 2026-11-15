namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Samples noise after scaling horizontal and vertical coordinates independently.
/// </summary>
[DensityFunction("minecraft:noise")]
public sealed class NoiseDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:noise";

    /// <summary>
    /// Gets the noise sampled by this function, read from <c>noise</c>.
    /// </summary>
    public required INoise Noise { get; init; }

    /// <summary>
    /// Gets the multiplier applied to X and Z before sampling, read from <c>xz_scale</c>.
    /// </summary>
    public required double XzScale { get; init; }

    /// <summary>
    /// Gets the multiplier applied to Y before sampling, read from <c>y_scale</c>.
    /// </summary>
    public required double YScale { get; init; }

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => -this.MaxValue;

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => this.Noise.MaxValue;

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => this.Noise.GetValue(x * this.XzScale, y * this.YScale, z * this.XzScale);

    /// <inheritdoc/>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new NoiseDensityFunction
    {
        Noise = visitor.VisitNoise(this.Noise),
        XzScale = this.XzScale,
        YScale = this.YScale
    });
}
