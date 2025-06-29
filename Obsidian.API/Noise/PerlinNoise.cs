namespace Obsidian.API.Noise;

public class PerlinNoise
{
    public readonly double MaxValue;

    private const int ROUND_OFF = 33554432;
    private readonly ImprovedNoise[] _noiseLevels;
    private readonly int _firstOctave;
    private readonly double[] _amplitudes;
    private readonly double _lowestFreqValueFactor;
    private readonly double _lowestFreqInputFactor;

    public static PerlinNoise Create(Random randomSource, int firstOctave, double[] amplitudes)
    {
        return new PerlinNoise(randomSource, Tuple.Create(firstOctave, amplitudes));
    }

    public PerlinNoise(Random randomSource, Tuple<int, double[]> configuration)
    {
        _firstOctave = configuration.Item1;
        _amplitudes = configuration.Item2.ToArray();

        int totalOctaves = _amplitudes.Count();
        int zeroOctaveIndex = -_firstOctave;
        _noiseLevels = new ImprovedNoise[totalOctaves];

        var baseNoise = new ImprovedNoise(randomSource);

        if (zeroOctaveIndex >= 0 && zeroOctaveIndex < totalOctaves)
        {
            double amplitude = _amplitudes.ElementAt(zeroOctaveIndex);
            if (amplitude != 0.0)
            {
                _noiseLevels[zeroOctaveIndex] = baseNoise;
            }
        }

        for (int i = zeroOctaveIndex - 1; i >= 0; i--)
        {
            if (i < totalOctaves)
            {
                double amplitude = _amplitudes.ElementAt(i);
                _noiseLevels[i] = amplitude != 0.0 ? new ImprovedNoise(randomSource) : null;
            }
        }

        _lowestFreqInputFactor = Math.Pow(2.0, -zeroOctaveIndex);
        _lowestFreqValueFactor = Math.Pow(2.0, totalOctaves - 1) / (Math.Pow(2.0, totalOctaves) - 1);
        MaxValue = EdgeValue(2.0);
    }

    public static Tuple<int, double[]> CreateConfigFromOctaves(IEnumerable<int> octaveSet)
    {
        int minOctave = -octaveSet.Min();
        int maxOctave = octaveSet.Max();
        int totalOctaves = minOctave + maxOctave + 1;

        var amplitudes = Enumerable.Repeat(0.0, totalOctaves).ToArray();

        foreach (int octave in octaveSet)
        {
            amplitudes[octave + minOctave] = 1.0;
        }

        return Tuple.Create(-minOctave, amplitudes);
    }

    public double GetValue(double x, double y, double z)
    {
        return GetValue(x, y, z, 0.0, 0.0, false);
    }

    public double GetValue(double x, double y, double z, double offsetX, double offsetY, bool useOffset)
    {
        double result = 0.0;
        double inputFactor = _lowestFreqInputFactor;
        double valueFactor = _lowestFreqValueFactor;

        foreach (var noise in _noiseLevels)
        {
            if (noise != null)
            {
                double noiseValue = noise.Noise(Wrap(x * inputFactor), useOffset ? -noise.yo : Wrap(y * inputFactor), Wrap(z * inputFactor), offsetX * inputFactor, offsetY * inputFactor);
                int ampIndex = Array.IndexOf(_noiseLevels, noise);
                result += _amplitudes[ampIndex] * noiseValue * valueFactor;
            }

            inputFactor *= 2.0;
            valueFactor /= 2.0;
        }

        return result;
    }

    private double EdgeValue(double scaleFactor)
    {
        double result = 0.0;
        double valueFactor = _lowestFreqValueFactor;

        foreach (var noise in _noiseLevels)
        {
            if (noise != null)
            {
                result += _amplitudes[_noiseLevels.ToList().IndexOf(noise)] * scaleFactor * valueFactor;
            }
            valueFactor /= 2.0;
        }

        return result;
    }

    public static double Wrap(double value) => value - Math.Floor(value / 3.3554432E7 + 0.5) * 3.3554432E7;

    public int FirstOctave => _firstOctave;

    public ImprovedNoise GetOctaveNoise(int index) => _noiseLevels[index];

    public IReadOnlyList<double> Amplitudes => _amplitudes;

    public double MaxBrokenValue(double scaleFactor) => EdgeValue(scaleFactor + 2.0);

}
