using Obsidian.API.World.Generator.RandomSources;
using System.Globalization;

namespace Obsidian.API.Noise;

/// <summary>
/// Multi-octave Perlin noise (vanilla <c>PerlinNoise</c>). Octave <c>i</c> samples at frequency
/// <c>2^(FirstOctave + i)</c> and is weighted by <c>Amplitudes[i]</c>; zero amplitudes skip the octave.
/// </summary>
public sealed class PerlinNoise
{
    private const double RoundOff = 33554432.0;

    /// <summary>Random values consumed by one <see cref="ImprovedNoise"/>: 3 doubles (2 ints each) + 256 ints.</summary>
    private const int ValuesPerOctave = 262;

    private readonly ImprovedNoise?[] noiseLevels;
    private readonly double[] amplitudes;
    private readonly double lowestFreqValueFactor;
    private readonly double lowestFreqInputFactor;

    public int FirstOctave { get; }

    public ReadOnlySpan<double> Amplitudes => this.amplitudes;

    /// <summary>Upper bound of <see cref="GetValue(double, double, double)"/>.</summary>
    public double MaxValue { get; }

    private PerlinNoise(IRandomSource random, int firstOctave, double[] amplitudes, bool useNewInitialization)
    {
        this.FirstOctave = firstOctave;
        this.amplitudes = amplitudes;

        var octaveCount = amplitudes.Length;
        var zeroOctaveIndex = -firstOctave;
        this.noiseLevels = new ImprovedNoise?[octaveCount];

        if (useNewInitialization)
        {
            var factory = random.ForkPositional();
            for (var i = 0; i < octaveCount; i++)
            {
                if (amplitudes[i] != 0.0)
                {
                    // Invariant formatting: some cultures use U+2212 as the negative sign.
                    var octave = (firstOctave + i).ToString(CultureInfo.InvariantCulture);
                    this.noiseLevels[i] = new ImprovedNoise(factory.FromHashOf("octave_" + octave));
                }
            }
        }
        else
        {
            if (zeroOctaveIndex < octaveCount - 1)
                throw new ArgumentException("Positive octaves are not supported by legacy initialization.", nameof(firstOctave));

            // Legacy initialization creates octave 0 first, then lower frequencies, from one shared random.
            var zeroOctave = new ImprovedNoise(random);
            if (zeroOctaveIndex >= 0 && zeroOctaveIndex < octaveCount && amplitudes[zeroOctaveIndex] != 0.0)
                this.noiseLevels[zeroOctaveIndex] = zeroOctave;

            for (var i = zeroOctaveIndex - 1; i >= 0; i--)
            {
                if (i < octaveCount && amplitudes[i] != 0.0)
                    this.noiseLevels[i] = new ImprovedNoise(random);
                else
                    random.ConsumeCount(ValuesPerOctave);
            }
        }

        this.lowestFreqInputFactor = Math.Pow(2.0, -zeroOctaveIndex);
        this.lowestFreqValueFactor = Math.Pow(2.0, octaveCount - 1) / (Math.Pow(2.0, octaveCount) - 1.0);
        this.MaxValue = this.EdgeValue(2.0);
    }

    /// <summary>Creates a noise whose octaves are seeded independently via <c>ForkPositional().FromHashOf("octave_N")</c>.</summary>
    public static PerlinNoise Create(IRandomSource random, int firstOctave, ReadOnlySpan<double> amplitudes) =>
        new(random, firstOctave, amplitudes.ToArray(), useNewInitialization: true);

    /// <summary>
    /// Creates a legacy-initialized noise with amplitude 1 for each octave in <paramref name="octaves"/>
    /// (all must be &lt;= 0). Used by <see cref="BlendedNoise"/>.
    /// </summary>
    public static PerlinNoise CreateLegacyForBlendedNoise(IRandomSource random, IEnumerable<int> octaves)
    {
        var (firstOctave, amplitudes) = MakeAmplitudes(octaves);
        return new PerlinNoise(random, firstOctave, amplitudes, useNewInitialization: false);
    }

    /// <summary>Creates a legacy-initialized noise for the legacy nether biome source.</summary>
    public static PerlinNoise CreateLegacyForLegacyNetherBiome(IRandomSource random, int firstOctave, ReadOnlySpan<double> amplitudes) =>
        new(random, firstOctave, amplitudes.ToArray(), useNewInitialization: false);

    public double GetValue(double x, double y, double z) => this.GetValue(x, y, z, 0.0, 0.0, false);

    /// <summary>
    /// Legacy sampling. <paramref name="yScale"/>/<paramref name="yMax"/> are passed (scaled per octave) to
    /// <see cref="ImprovedNoise.Noise(double, double, double, double, double)"/>; when <paramref name="useFixedY"/>
    /// is set each octave samples at its own fixed y (<c>-Yo</c>) instead of <paramref name="y"/>.
    /// </summary>
    public double GetValue(double x, double y, double z, double yScale, double yMax, bool useFixedY)
    {
        var value = 0.0;
        var inputFactor = this.lowestFreqInputFactor;
        var valueFactor = this.lowestFreqValueFactor;

        for (var i = 0; i < this.noiseLevels.Length; i++)
        {
            var noise = this.noiseLevels[i];
            if (noise is not null)
            {
                var sample = noise.Noise(
                    Wrap(x * inputFactor),
                    useFixedY ? -noise.Yo : Wrap(y * inputFactor),
                    Wrap(z * inputFactor),
                    yScale * inputFactor,
                    yMax * inputFactor);
                value += this.amplitudes[i] * sample * valueFactor;
            }

            inputFactor *= 2.0;
            valueFactor /= 2.0;
        }

        return value;
    }

    /// <summary>Upper bound used by <see cref="BlendedNoise"/> for samples scaled by <paramref name="scale"/>.</summary>
    public double MaxBrokenValue(double scale) => this.EdgeValue(scale + 2.0);

    /// <summary>
    /// Returns the octave with the <paramref name="index"/>-th highest frequency (0 = highest), or null if its
    /// amplitude is zero. This is vanilla's (reversed) indexing.
    /// </summary>
    public ImprovedNoise? GetOctaveNoise(int index) => this.noiseLevels[this.noiseLevels.Length - 1 - index];

    /// <summary>Wraps a coordinate into <c>[-2^24, 2^24]</c> to keep precision for far-out samples.</summary>
    public static double Wrap(double value) => value - Mth.LFloor(value / RoundOff + 0.5) * RoundOff;

    private double EdgeValue(double scale)
    {
        var value = 0.0;
        var valueFactor = this.lowestFreqValueFactor;

        for (var i = 0; i < this.noiseLevels.Length; i++)
        {
            if (this.noiseLevels[i] is not null)
                value += this.amplitudes[i] * scale * valueFactor;

            valueFactor /= 2.0;
        }

        return value;
    }

    private static (int FirstOctave, double[] Amplitudes) MakeAmplitudes(IEnumerable<int> octaves)
    {
        var sorted = new SortedSet<int>(octaves);
        if (sorted.Count == 0)
            throw new ArgumentException("Need some octaves!", nameof(octaves));

        var lowest = -sorted.Min;
        var amplitudes = new double[lowest + sorted.Max + 1];
        foreach (var octave in sorted)
            amplitudes[octave + lowest] = 1.0;

        return (-lowest, amplitudes);
    }
}
