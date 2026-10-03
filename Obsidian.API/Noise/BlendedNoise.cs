using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.Noise;

/// <summary>
/// Vanilla <c>BlendedNoise</c> (the <c>minecraft:old_blended_noise</c> density function): a main noise selects
/// between two high-octave limit noises, giving pre-1.18 style terrain.
/// </summary>
/// <remarks>
/// Instances from <see cref="CreateUnseeded"/> carry the configuration only; call <see cref="WithNewRandom"/> with
/// the world's <c>"minecraft:terrain"</c> random before sampling, as vanilla does.
/// </remarks>
public sealed class BlendedNoise
{
    private readonly PerlinNoise minLimitNoise;
    private readonly PerlinNoise maxLimitNoise;
    private readonly PerlinNoise mainNoise;

    // The octaves of each noise from the highest frequency down, the order Compute walks them in.
    private readonly ImprovedNoise?[] minLimitOctaves;
    private readonly ImprovedNoise?[] maxLimitOctaves;
    private readonly ImprovedNoise?[] mainOctaves;
    private readonly double xzMultiplier;
    private readonly double yMultiplier;
    private readonly double xzFactor;
    private readonly double yFactor;
    private readonly double smearScaleMultiplier;
    private readonly double xzScale;
    private readonly double yScale;

    public double MinValue => -this.MaxValue;

    public double MaxValue { get; }

    /// <summary>Creates a noise seeded from <paramref name="random"/>. Prefer <see cref="CreateUnseeded"/> + <see cref="WithNewRandom"/>.</summary>
    public BlendedNoise(IRandomSource random, double xzScale, double yScale, double xzFactor, double yFactor, double smearScaleMultiplier)
    {
        this.minLimitNoise = PerlinNoise.CreateLegacyForBlendedNoise(random, Enumerable.Range(-15, 16));
        this.maxLimitNoise = PerlinNoise.CreateLegacyForBlendedNoise(random, Enumerable.Range(-15, 16));
        this.mainNoise = PerlinNoise.CreateLegacyForBlendedNoise(random, Enumerable.Range(-7, 8));
        this.minLimitOctaves = Octaves(this.minLimitNoise, 16);
        this.maxLimitOctaves = Octaves(this.maxLimitNoise, 16);
        this.mainOctaves = Octaves(this.mainNoise, 8);
        this.xzScale = xzScale;
        this.yScale = yScale;
        this.xzFactor = xzFactor;
        this.yFactor = yFactor;
        this.smearScaleMultiplier = smearScaleMultiplier;
        this.xzMultiplier = 684.412 * xzScale;
        this.yMultiplier = 684.412 * yScale;
        this.MaxValue = this.minLimitNoise.MaxBrokenValue(this.yMultiplier);
    }

    /// <summary>Creates a placeholder instance seeded with <c>new XoroshiroRandomSource(0)</c>, like vanilla's codec.</summary>
    public static BlendedNoise CreateUnseeded(double xzScale, double yScale, double xzFactor, double yFactor, double smearScaleMultiplier) =>
        new(new XoroshiroRandomSource(0L), xzScale, yScale, xzFactor, yFactor, smearScaleMultiplier);

    /// <summary>Returns a copy of this configuration seeded from <paramref name="random"/>.</summary>
    public BlendedNoise WithNewRandom(IRandomSource random) =>
        new(random, this.xzScale, this.yScale, this.xzFactor, this.yFactor, this.smearScaleMultiplier);

    public double Compute(int blockX, int blockY, int blockZ)
    {
        var limitX = blockX * this.xzMultiplier;
        var limitY = blockY * this.yMultiplier;
        var limitZ = blockZ * this.xzMultiplier;
        var mainX = limitX / this.xzFactor;
        var mainY = limitY / this.yFactor;
        var mainZ = limitZ / this.xzFactor;
        var limitSmear = this.yMultiplier * this.smearScaleMultiplier;
        var mainSmear = limitSmear / this.yFactor;

        var minLimit = 0.0;
        var maxLimit = 0.0;
        var main = 0.0;
        // Scales are powers of two, so multiplying by the inverse divides exactly.
        var scale = 1.0;
        var inverseScale = 1.0;

        foreach (var noise in this.mainOctaves)
        {
            if (noise is not null)
            {
                main += noise.Noise(
                    PerlinNoise.Wrap(mainX * scale),
                    PerlinNoise.Wrap(mainY * scale),
                    PerlinNoise.Wrap(mainZ * scale),
                    mainSmear * scale,
                    mainY * scale) * inverseScale;
            }

            scale /= 2.0;
            inverseScale *= 2.0;
        }

        var blend = (main / 10.0 + 1.0) / 2.0;
        var onlyMax = blend >= 1.0;
        var onlyMin = blend <= 0.0;
        scale = 1.0;
        inverseScale = 1.0;
        var minLimitOctaves = this.minLimitOctaves;
        var maxLimitOctaves = this.maxLimitOctaves.AsSpan(0, minLimitOctaves.Length);

        for (var i = 0; i < minLimitOctaves.Length; i++)
        {
            var x = PerlinNoise.Wrap(limitX * scale);
            var y = PerlinNoise.Wrap(limitY * scale);
            var z = PerlinNoise.Wrap(limitZ * scale);
            var smear = limitSmear * scale;

            if (!onlyMax)
            {
                var minNoise = minLimitOctaves[i];
                if (minNoise is not null)
                    minLimit += minNoise.Noise(x, y, z, smear, limitY * scale) * inverseScale;
            }

            if (!onlyMin)
            {
                var maxNoise = maxLimitOctaves[i];
                if (maxNoise is not null)
                    maxLimit += maxNoise.Noise(x, y, z, smear, limitY * scale) * inverseScale;
            }

            scale /= 2.0;
            inverseScale *= 2.0;
        }

        return Mth.ClampedLerp(blend, minLimit / 512.0, maxLimit / 512.0) / 128.0;
    }

    private static ImprovedNoise?[] Octaves(PerlinNoise noise, int count) =>
        Enumerable.Range(0, count).Select(noise.GetOctaveNoise).ToArray();
}
