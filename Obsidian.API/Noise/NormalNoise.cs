using Obsidian.API.World.Generator.RandomSources;
using System.Runtime.Intrinsics;

namespace Obsidian.API.Noise;

/// <summary>
/// Vanilla <c>NormalNoise</c>: the sum of two <see cref="PerlinNoise"/> instances (the second sampled at a slightly
/// larger scale), normalized so the result has a deviation of about 1/3. This is what <c>worldgen/noise</c>
/// definitions (<c>firstOctave</c> + <c>amplitudes</c>) instantiate.
/// </summary>
public sealed class NormalNoise
{
    private const double InputFactor = 1.0181268882175227;

    private readonly double valueFactor;
    private readonly PerlinNoise first;
    private readonly PerlinNoise second;

    /// <summary>Upper bound of <see cref="GetValue"/>.</summary>
    public double MaxValue { get; }

    private NormalNoise(IRandomSource random, int firstOctave, ReadOnlySpan<double> amplitudes, bool useModernInitialization)
    {
        if (useModernInitialization)
        {
            this.first = PerlinNoise.Create(random, firstOctave, amplitudes);
            this.second = PerlinNoise.Create(random, firstOctave, amplitudes);
        }
        else
        {
            this.first = PerlinNoise.CreateLegacyForLegacyNetherBiome(random, firstOctave, amplitudes);
            this.second = PerlinNoise.CreateLegacyForLegacyNetherBiome(random, firstOctave, amplitudes);
        }

        var minOctave = int.MaxValue;
        var maxOctave = int.MinValue;
        for (var i = 0; i < amplitudes.Length; i++)
        {
            if (amplitudes[i] != 0.0)
            {
                minOctave = Math.Min(minOctave, i);
                maxOctave = Math.Max(maxOctave, i);
            }
        }

        // Two summed noises: each contributes half of the target deviation (1/6).
        this.valueFactor = 0.16666666666666666 / ExpectedDeviation(unchecked(maxOctave - minOctave));
        this.MaxValue = (this.first.MaxValue + this.second.MaxValue) * this.valueFactor;
    }

    /// <summary>Creates a noise with modern (positional) octave seeding. Consumes 4 longs from <paramref name="random"/>.</summary>
    public static NormalNoise Create(IRandomSource random, int firstOctave, ReadOnlySpan<double> amplitudes) =>
        new(random, firstOctave, amplitudes, useModernInitialization: true);

    /// <summary>Creates a noise with legacy octave seeding, as used by the legacy nether biome source.</summary>
    public static NormalNoise CreateLegacyNetherBiome(IRandomSource random, int firstOctave, ReadOnlySpan<double> amplitudes) =>
        new(random, firstOctave, amplitudes, useModernInitialization: false);

    public double GetValue(double x, double y, double z)
    {
        var scaledX = x * InputFactor;
        var scaledY = y * InputFactor;
        var scaledZ = z * InputFactor;
        return (this.first.GetValue(x, y, z) + this.second.GetValue(scaledX, scaledY, scaledZ)) * this.valueFactor;
    }

    /// <summary>
    /// <see cref="GetValue"/> at four positions, one per lane, with the same arithmetic in each (see
    /// <see cref="ImprovedNoiseLanes"/>). Only where <see cref="ImprovedNoiseLanes.IsSupported"/>.
    /// </summary>
    internal Vector256<double> GetValue(Vector256<double> x, Vector256<double> y, Vector256<double> z)
    {
        var factor = Vector256.Create(InputFactor);
        return (this.first.GetValue(x, y, z) + this.second.GetValue(x * factor, y * factor, z * factor)) * Vector256.Create(this.valueFactor);
    }

    private static double ExpectedDeviation(int octaveSpan) => 0.1 * (1.0 + 1.0 / (octaveSpan + 1));
}
