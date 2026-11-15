using Obsidian.API.Noise;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// The pre-1.18 3D terrain noise, still used as the base of overworld and nether terrain.
/// </summary>
/// <remarks>
/// Registry instances are unseeded (vanilla seeds them with 0). World generation binds a seeded
/// copy with <see cref="WithRandom"/>.
/// </remarks>
[DensityFunction("minecraft:old_blended_noise")]
public class OldBlendedNoiseDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:old_blended_noise";

    /// <summary>
    /// Gets the vertical noise smear multiplier, read from <c>smear_scale_multiplier</c>.
    /// </summary>
    public required double SmearScaleMultiplier { get; init; }

    /// <summary>
    /// Gets the horizontal divisor used by the blending noise, read from <c>xz_factor</c>.
    /// </summary>
    public required double XzFactor { get; init; }

    /// <summary>
    /// Gets the horizontal sampling scale, read from <c>xz_scale</c>.
    /// </summary>
    public required double XzScale { get; init; }

    /// <summary>
    /// Gets the vertical divisor used by the blending noise, read from <c>y_factor</c>.
    /// </summary>
    public required double YFactor { get; init; }

    /// <summary>
    /// Gets the vertical sampling scale, read from <c>y_scale</c>.
    /// </summary>
    public required double YScale { get; init; }

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => this.Noise.MinValue;

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => this.Noise.MaxValue;

    private BlendedNoise Noise
    {
        get => field ??= BlendedNoise.CreateUnseeded(this.XzScale, this.YScale, this.XzFactor, this.YFactor, this.SmearScaleMultiplier);
        init;
    }

    /// <summary>
    /// Returns a copy of this function seeded from <paramref name="random"/>.
    /// </summary>
    public OldBlendedNoiseDensityFunction WithRandom(IRandomSource random) => new()
    {
        SmearScaleMultiplier = this.SmearScaleMultiplier,
        XzFactor = this.XzFactor,
        XzScale = this.XzScale,
        YFactor = this.YFactor,
        YScale = this.YScale,
        Noise = this.Noise.WithNewRandom(random)
    };

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => this.Noise.Compute((int)x, (int)y, (int)z);

    /// <summary>The noise <see cref="GetValue"/> samples.</summary>
    internal BlendedNoise BlendedNoise => this.Noise;
}
