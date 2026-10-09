using Obsidian.API.World.Generator.RandomSources;
using System.Runtime.Intrinsics;

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

    // The octaves sampled four positions at a time, in the same order, made on first use.
    private ImprovedNoiseLanes?[]? mainLanes;
    private ImprovedNoiseLanes?[]? minLimitLanes;
    private ImprovedNoiseLanes?[]? maxLimitLanes;

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

    /// <summary>
    /// <see cref="Compute"/> at four block positions, one per lane, with the same arithmetic in each (see
    /// <see cref="ImprovedNoiseLanes"/>). Only where <see cref="ImprovedNoiseLanes.IsSupported"/>.
    /// </summary>
    /// <param name="blockX">Block X of each lane, a whole number.</param>
    /// <param name="blockY">Block Y of each lane, a whole number.</param>
    /// <param name="blockZ">Block Z of each lane, a whole number.</param>
    internal Vector256<double> Compute(Vector256<double> blockX, Vector256<double> blockY, Vector256<double> blockZ)
    {
        // Threads racing to make the samplers make equal ones.
        this.mainLanes ??= Lanes(this.mainOctaves);
        this.minLimitLanes ??= Lanes(this.minLimitOctaves);
        this.maxLimitLanes ??= Lanes(this.maxLimitOctaves);

        var limitX = blockX * this.xzMultiplier;
        var limitY = blockY * this.yMultiplier;
        var limitZ = blockZ * this.xzMultiplier;
        var mainX = limitX / this.xzFactor;
        var mainY = limitY / this.yFactor;
        var mainZ = limitZ / this.xzFactor;
        var limitSmear = this.yMultiplier * this.smearScaleMultiplier;
        var mainSmear = limitSmear / this.yFactor;

        var main = Sum(this.mainLanes, this.mainOctaves, mainX, mainY, mainZ, mainSmear);
        var blend = (main / 10.0 + Vector256<double>.One) / 2.0;

        // Lanes only skip a limit noise when it doesn't matter, so the octaves are skipped when no lane needs them.
        var onlyMax = Vector256.GreaterThanOrEqual(blend, Vector256<double>.One);
        var onlyMin = Vector256.LessThanOrEqual(blend, Vector256<double>.Zero);
        var minLimit = Vector256.EqualsAll(onlyMax.AsInt64(), Vector256<long>.AllBitsSet)
            ? Vector256<double>.Zero
            : Vector256.ConditionalSelect(onlyMax, Vector256<double>.Zero, Sum(this.minLimitLanes, this.minLimitOctaves, limitX, limitY, limitZ, limitSmear));
        var maxLimit = Vector256.EqualsAll(onlyMin.AsInt64(), Vector256<long>.AllBitsSet)
            ? Vector256<double>.Zero
            : Vector256.ConditionalSelect(onlyMin, Vector256<double>.Zero, Sum(this.maxLimitLanes, this.maxLimitOctaves, limitX, limitY, limitZ, limitSmear));

        // Mth.ClampedLerp.
        var start = minLimit / 512.0;
        var end = maxLimit / 512.0;
        var lerped = Vector256.ConditionalSelect(Vector256.GreaterThan(blend, Vector256<double>.One), end, start + blend * (end - start));
        return Vector256.ConditionalSelect(Vector256.LessThan(blend, Vector256<double>.Zero), start, lerped) / 128.0;
    }

    // A noise's octaves summed like Compute does: octave i at 2^-i times the position, times 2^i.
    private static Vector256<double> Sum(ImprovedNoiseLanes?[] lanes, ImprovedNoise?[] octaves, Vector256<double> x, Vector256<double> y,
        Vector256<double> z, double smear)
    {
        var sum = Vector256<double>.Zero;
        var scale = 1.0;
        var inverseScale = 1.0;

        for (var i = 0; i < lanes.Length; i++)
        {
            if (lanes[i] is ImprovedNoiseLanes noise)
            {
                var scaledY = y * scale;
                if (!noise.TryNoise(PerlinNoise.WrapLanes(x * scale), PerlinNoise.WrapLanes(scaledY), PerlinNoise.WrapLanes(z * scale),
                    Vector256.Create(smear * scale), scaledY, out var sample))
                {
                    // A smear out of the int range, which only the scalar path saturates like vanilla.
                    sample = Vector256.Create(
                        Scalar(octaves[i]!, x[0], y[0], z[0], smear, scale), Scalar(octaves[i]!, x[1], y[1], z[1], smear, scale),
                        Scalar(octaves[i]!, x[2], y[2], z[2], smear, scale), Scalar(octaves[i]!, x[3], y[3], z[3], smear, scale));
                }

                sum += sample * inverseScale;
            }

            scale /= 2.0;
            inverseScale *= 2.0;
        }

        return sum;
    }

    private static double Scalar(ImprovedNoise noise, double x, double y, double z, double smear, double scale) =>
        noise.Noise(PerlinNoise.Wrap(x * scale), PerlinNoise.Wrap(y * scale), PerlinNoise.Wrap(z * scale), smear * scale, y * scale);

    private static ImprovedNoiseLanes?[] Lanes(ImprovedNoise?[] octaves) =>
        [.. octaves.Select(octave => octave is null ? null : new ImprovedNoiseLanes(octave))];

    private static ImprovedNoise?[] Octaves(PerlinNoise noise, int count) =>
        Enumerable.Range(0, count).Select(noise.GetOctaveNoise).ToArray();
}
