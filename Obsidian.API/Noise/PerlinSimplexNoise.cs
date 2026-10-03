using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.Noise;

/// <summary>
/// Multi-octave 2D simplex noise (vanilla <c>PerlinSimplexNoise</c>), used for biome temperature variation:
/// <c>new PerlinSimplexNoise(new WorldgenRandom(new LegacyRandomSource(1234L)), [0])</c>.
/// </summary>
public sealed class PerlinSimplexNoise
{
    /// <summary>Random values consumed by one <see cref="SimplexNoise"/>: 3 doubles (2 ints each) + 256 ints.</summary>
    private const int ValuesPerOctave = 262;

    /// <summary>Ordered from highest to lowest frequency.</summary>
    private readonly SimplexNoise?[] noiseLevels;
    private readonly double highestFreqValueFactor;
    private readonly double highestFreqInputFactor;

    /// <param name="random">Source for octave 0 and the negative (lower-frequency) octaves.</param>
    /// <param name="octaves">Octaves to include; octave <c>n</c> samples at frequency <c>2^n</c>.</param>
    public PerlinSimplexNoise(IRandomSource random, IEnumerable<int> octaves)
    {
        var sorted = new SortedSet<int>(octaves);
        if (sorted.Count == 0)
            throw new ArgumentException("Need some octaves!", nameof(octaves));

        var lowest = -sorted.Min;
        var highest = sorted.Max;
        var octaveCount = lowest + highest + 1;

        var zeroOctave = new SimplexNoise(random);
        var zeroOctaveIndex = highest;
        this.noiseLevels = new SimplexNoise?[octaveCount];
        if (zeroOctaveIndex >= 0 && zeroOctaveIndex < octaveCount && sorted.Contains(0))
            this.noiseLevels[zeroOctaveIndex] = zeroOctave;

        // Negative octaves come from the shared random.
        for (var i = zeroOctaveIndex + 1; i < octaveCount; i++)
        {
            if (i >= 0 && sorted.Contains(zeroOctaveIndex - i))
                this.noiseLevels[i] = new SimplexNoise(random);
            else
                random.ConsumeCount(ValuesPerOctave);
        }

        // Positive octaves come from a random seeded by sampling the zero octave.
        if (highest > 0)
        {
            var seed = (long)(zeroOctave.GetValue(zeroOctave.Xo, zeroOctave.Yo, zeroOctave.Zo) * 9.223372036854776E18);
            var positiveRandom = new WorldgenRandom(new LegacyRandomSource(seed));
            for (var i = zeroOctaveIndex - 1; i >= 0; i--)
            {
                if (i < octaveCount && sorted.Contains(zeroOctaveIndex - i))
                    this.noiseLevels[i] = new SimplexNoise(positiveRandom);
                else
                    positiveRandom.ConsumeCount(ValuesPerOctave);
            }
        }

        this.highestFreqInputFactor = Math.Pow(2.0, highest);
        this.highestFreqValueFactor = 1.0 / (Math.Pow(2.0, octaveCount) - 1.0);
    }

    /// <summary>
    /// Samples the noise at (x, y). When <paramref name="useNoiseOffsets"/> is set, each octave is shifted by its
    /// own random offset.
    /// </summary>
    public double GetValue(double x, double y, bool useNoiseOffsets)
    {
        var value = 0.0;
        var inputFactor = this.highestFreqInputFactor;
        var valueFactor = this.highestFreqValueFactor;

        foreach (var noise in this.noiseLevels)
        {
            if (noise is not null)
            {
                value += noise.GetValue(
                    x * inputFactor + (useNoiseOffsets ? noise.Xo : 0.0),
                    y * inputFactor + (useNoiseOffsets ? noise.Yo : 0.0)) * valueFactor;
            }

            inputFactor /= 2.0;
            valueFactor *= 2.0;
        }

        return value;
    }
}
