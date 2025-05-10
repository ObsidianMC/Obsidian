using Obsidian.API.Noise;

namespace Obsidian.API.World.Generator.Noise;
public partial class BaseNoise : INoise
{
    public string Type => "minecraft:base_noise";

    public required double[] Amplitudes { get; init; }

    public required double FirstOctave { get; init; }

    public int Seed { get; init; }

    public double MinValue => -MaxValue;

    public double MaxValue { 
        get
        {
            if (!_initialized)
            {
                Create();
            }
            return field;
        }
        private set; 
    }

    private bool _initialized = false;

    private const double InputFactor = 1.0181268882175227;

    private double _valueFactor;
    private PerlinNoise _primaryNoise;
    private PerlinNoise _secondaryNoise;


    private static double ExpectedDeviation(int range)
    {
        return 0.1 * (1.0 + 1.0 / (range + 1.0));
    }

    public void Create()
    {
        _primaryNoise = PerlinNoise.Create(new Random(Seed+3), (int)FirstOctave, Amplitudes);
        _secondaryNoise = PerlinNoise.Create(new Random(Seed+4), (int)FirstOctave, Amplitudes);

        int minIndex = int.MaxValue;
        int maxIndex = int.MinValue;

        for (int i = 0; i < Amplitudes.ToList().Count; i++)
        {
            if (Amplitudes[i] != 0.0)
            {
                minIndex = Math.Min(minIndex, i);
                maxIndex = Math.Max(maxIndex, i);
            }
        }
        _valueFactor = 0.16666666666666666 / ExpectedDeviation(maxIndex - minIndex);
        MaxValue = _primaryNoise.MaxValue * _secondaryNoise.MaxValue * _valueFactor;
        _initialized = true;
    }

    public double GetValue(double x, double y, double z)
    {
        if (!_initialized)
        {
            Create();
        }

        double scaledX = x * InputFactor;
        double scaledY = y * InputFactor;
        double scaledZ = z * InputFactor;

        double primaryValue = _primaryNoise.GetValue(x, y, z);
        double secondaryValue = _secondaryNoise.GetValue(scaledX, scaledY, scaledZ);

        return (primaryValue + secondaryValue) * _valueFactor;
    }
}
