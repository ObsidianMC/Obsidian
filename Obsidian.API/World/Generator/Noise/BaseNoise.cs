using SharpNoise;

namespace Obsidian.API.World.Generator.Noise;
public partial class BaseNoise : INoise
{
    public string Type => "minecraft:base_noise";

    public required List<double> Amplitudes { get; init; }

    public required double FirstOctave { get; init; }

    public required int Seed { get; init; }

    public double GetValue(double x, double y, double z)
    {
        int octaves = Amplitudes.Count;
        double result = 0.0;
        for (int i = 0; i < octaves; i++)
        {
            int s = (Seed + i) & 0x7FFFFFFF;
            double noise1 = NoiseGenerator.GradientCoherentNoise3D(x, y, z, s, NoiseQuality.Standard);
            double noise2 = NoiseGenerator.GradientCoherentNoise3D(x, y, z, s+octaves, NoiseQuality.Standard);
            double noise = noise1 + noise2 / 2.0D;
            double persistence = Amplitudes[i] * Math.Pow(2, octaves - i - 1) / (Math.Pow(2, octaves) - 1);
            result += noise * persistence;
            double lacunarity = Math.Pow(2, -FirstOctave + i);
            x *= lacunarity;
            y *= lacunarity;
            z *= lacunarity;
        }

        return 10 * result / (3 * (1 + (1 / (octaves - 2))));
    }
}
