using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Generators.Density;

/// <summary>How a noise scales its octaves (<c>normalize</c> in 26.3's noise JSON).</summary>
internal enum NoiseNormalization
{
    /// <summary><c>false</c>: the octave amplitudes are used as given.</summary>
    Disabled,

    /// <summary><c>true</c>, the default: the octaves are scaled to a standard deviation of a third.</summary>
    Enabled,

    /// <summary>
    /// <c>"legacy"</c>: like <see cref="Enabled"/>, but scaled like the noises of 1.18 to 26.2 with the same
    /// amplitudes.
    /// </summary>
    Legacy
}

/// <summary>
/// A 26.3 noise (<c>worldgen/noise</c>): <c>base_octave</c>, <c>base_amplitude</c>, <c>octave_count</c>,
/// <c>amplitude_modifiers</c> (empty for all 1) and <c>normalize</c>.
/// </summary>
/// <remarks>
/// <see cref="Create"/> matches vanilla's NormalNoise: octave i has frequency 2^(base_octave + i) and half the previous
/// amplitude, octaves with a zero modifier are skipped, and each octave is two gradient noises, the second at a
/// slightly higher frequency, seeded from <c>octave_&lt;index&gt;</c>. The normalisation is computed in double and
/// narrowed to a float amplitude per octave, like vanilla's.
/// </remarks>
internal sealed record NoiseParameters(
    int BaseOctave,
    double BaseAmplitude,
    int OctaveCount,
    NoiseNormalization Normalize,
    ImmutableArray<double> AmplitudeModifiers)
{
    // Vanilla's constants: the second noise's frequency factor, the target deviation and a gradient noise's deviation.
    private const double InputFactor = 1.0181268882175227;
    private const double TargetDeviation = 1.0 / 3.0;
    private const double PerlinDeviation = 0.2702247831245211;

    private readonly record struct Octave(int Index, double Frequency, double Amplitude);

    /// <summary>
    /// Builds the noise for a world from the random its identifier seeds
    /// (<c>RandomState.random.fromHashOf(identifier)</c>).
    /// </summary>
    public NoiseStack Create(IRandomSource random)
    {
        var octaves = this.BuildOctaves(this.BaseAmplitude, this.Normalize != NoiseNormalization.Disabled);
        var normalization = this.NormalizationFactor(octaves);

        var first = random.ForkPositional();
        var second = random.ForkPositional();
        var layers = new List<NoiseStack.Layer>();

        foreach (var octave in octaves)
        {
            var seed = $"octave_{octave.Index}";
            var amplitude = (float)(normalization * octave.Amplitude);

            layers.Add(new(new PerlinNoise(first.FromHashOf(seed)), octave.Frequency, amplitude));
            layers.Add(new(new PerlinNoise(second.FromHashOf(seed)), octave.Frequency * InputFactor, amplitude));
        }

        return new NoiseStack(layers);
    }

    /// <summary>
    /// Builds the noise the way vanilla builds the Nether's biome noises (<c>nether/temperature</c> and
    /// <c>nether/vegetation</c>), with 1.16's octaves: both noises draw their octaves from one legacy random seeded
    /// from the world seed, highest frequency first, skipping the draws of zero octaves.
    /// </summary>
    public NoiseStack CreateLegacyNetherBiome(IRandomSource random)
    {
        var octaves = this.BuildOctaves(this.BaseAmplitude, this.Normalize != NoiseNormalization.Disabled);
        var amplitude = (float)(this.NormalizationFactor(octaves) * this.BaseAmplitude);

        var first = this.CreateLegacyOctaves(random);
        var second = this.CreateLegacyOctaves(random);
        var layers = new List<NoiseStack.Layer>();

        foreach (var layer in first)
            layers.Add(layer with { Amplitude = layer.Amplitude * amplitude });

        foreach (var layer in second)
        {
            var frequency = layer.Frequency * InputFactor;
            layers.Add(new NoiseStack.Layer(layer.Noise, frequency, layer.Amplitude * amplitude));
        }

        return new NoiseStack(layers);
    }

    // Vanilla's LegacyFbmInitializer: the octave at frequency 1 first, then the lower ones down to the first octave.
    private List<NoiseStack.Layer> CreateLegacyOctaves(IRandomSource random)
    {
        var count = this.OctaveCount;
        var zeroOctave = -this.BaseOctave;
        if (zeroOctave < count - 1)
            throw new NotSupportedException("Legacy Nether biome noises can't have positive octaves.");

        var noises = new PerlinNoise?[count];
        var frequencyOne = new PerlinNoise(random);

        if (zeroOctave < count && this.Modifier(zeroOctave) != 0.0)
            noises[zeroOctave] = frequencyOne;

        for (var i = zeroOctave - 1; i >= 0; i--)
        {
            if (i < count && this.Modifier(i) != 0.0)
                noises[i] = new PerlinNoise(random);
            else
                random.ConsumeCount(262);
        }

        var frequency = Math.Pow(2.0, -zeroOctave);
        var valueFactor = Math.Pow(2.0, count - 1) / (Math.Pow(2.0, count) - 1.0);
        var layers = new List<NoiseStack.Layer>();

        for (var i = 0; i < count; i++)
        {
            if (noises[i] is { } noise)
                layers.Add(new NoiseStack.Layer(noise, frequency, (float)(valueFactor * this.Modifier(i))));

            frequency *= 2.0;
            valueFactor /= 2.0;
        }

        return layers;
    }

    private List<Octave> BuildOctaves(double baseAmplitude, bool normalize)
    {
        var frequency = Math.Pow(2.0, this.BaseOctave);
        var amplitude = baseAmplitude;

        if (normalize)
            amplitude *= Math.Pow(0.5, -(this.OctaveCount - 1)) / (Math.Pow(0.5, -this.OctaveCount) - 1.0);

        var octaves = new List<Octave>(this.OctaveCount);
        for (var i = 0; i < this.OctaveCount; i++)
        {
            var modifier = this.Modifier(i);
            if (modifier != 0.0)
                octaves.Add(new Octave(this.BaseOctave + i, frequency, amplitude * modifier));

            frequency *= 2.0;
            amplitude *= 0.5;
        }

        return octaves;
    }

    private double NormalizationFactor(List<Octave> octaves)
    {
        var targetAmplitude = TotalAmplitude(octaves);
        var factor = ComputeNormalizationFactor(targetAmplitude, octaves);

        if (this.Normalize == NoiseNormalization.Legacy && factor != 0.0)
            factor = this.LegacyNormalizationFactor(this.BaseAmplitude);

        return factor;
    }

    private static double ComputeNormalizationFactor(double targetAmplitude, List<Octave> octaves)
    {
        var variance = 0.0;
        foreach (var octave in octaves)
        {
            var deviation = PerlinDeviation * Math.Abs(octave.Amplitude);
            variance += deviation * deviation;
        }

        var inputDeviation = Math.Sqrt(variance);
        if (inputDeviation == 0.0)
            return 0.0;

        var inputSumDeviation = inputDeviation * Math.Sqrt(2.0);
        return targetAmplitude * TargetDeviation / inputSumDeviation;
    }

    // The factor 1.18's noise gave these amplitudes: from the span between the first and last non-zero octave.
    private double LegacyNormalizationFactor(double baseAmplitude)
    {
        var minOctave = int.MaxValue;
        var maxOctave = int.MinValue;

        for (var i = 0; i < this.OctaveCount; i++)
        {
            if (this.Modifier(i) == 0.0)
                continue;

            minOctave = Math.Min(minOctave, i);
            maxOctave = Math.Max(maxOctave, i);
        }

        var expectedDeviation = 0.1 * (1.0 + 1.0 / (maxOctave - minOctave + 1));
        return baseAmplitude * 0.5 * TargetDeviation / expectedDeviation;
    }

    // Vanilla sums with DoubleStream.sum, which is Kahan-compensated; a plain sum can differ in the last bit.
    private static double TotalAmplitude(List<Octave> octaves)
    {
        var sum = 0.0;
        var compensation = 0.0;

        foreach (var octave in octaves)
        {
            var term = Math.Abs(octave.Amplitude) - compensation;
            var next = sum + term;

            compensation = (next - sum) - term;
            sum = next;
        }

        return sum - compensation;
    }

    private double Modifier(int index) => this.AmplitudeModifiers.IsEmpty ? 1.0 : this.AmplitudeModifiers[index];
}
