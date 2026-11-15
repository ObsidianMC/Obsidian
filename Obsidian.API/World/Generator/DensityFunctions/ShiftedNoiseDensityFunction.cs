namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Samples noise with density-driven coordinate offsets applied after scaling.
/// </summary>
[DensityFunction("minecraft:shifted_noise")]
public sealed class ShiftedNoiseDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:shifted_noise";

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
    /// Gets the function supplying the X offset after coordinate scaling.
    /// </summary>
    public required IDensityFunction ShiftX { get; init; }
    /// <summary>
    /// Gets the function supplying the Y offset after coordinate scaling.
    /// </summary>
    public required IDensityFunction ShiftY { get; init; }
    /// <summary>
    /// Gets the function supplying the Z offset after coordinate scaling.
    /// </summary>
    public required IDensityFunction ShiftZ { get; init; }

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => -this.MaxValue;

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => this.Noise.MaxValue;

    // Shifts are added after scaling, as in vanilla.
    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => this.Noise.GetValue(
        x * this.XzScale + this.ShiftX.GetValue(x, y, z),
        y * this.YScale + this.ShiftY.GetValue(x, y, z),
        z * this.XzScale + this.ShiftZ.GetValue(x, y, z));

    /// <inheritdoc/>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new ShiftedNoiseDensityFunction
    {
        Noise = visitor.VisitNoise(this.Noise),
        XzScale = this.XzScale,
        YScale = this.YScale,
        ShiftX = visitor.Map(this.ShiftX),
        ShiftY = visitor.Map(this.ShiftY),
        ShiftZ = visitor.Map(this.ShiftZ)
    });
}
