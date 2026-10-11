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
    public string Type => "minecraft:old_blended_noise";

    public required double SmearScaleMultiplier { get; init; }

    public required double XzFactor { get; init; }

    public required double XzScale { get; init; }

    public required double YFactor { get; init; }

    public required double YScale { get; init; }

    public double MinValue => this.Noise.MinValue;

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

    public double GetValue(double x, double y, double z) => this.Noise.Compute((int)x, (int)y, (int)z);

    /// <summary>The noise <see cref="GetValue"/> samples.</summary>
    internal BlendedNoise BlendedNoise => this.Noise;
}
