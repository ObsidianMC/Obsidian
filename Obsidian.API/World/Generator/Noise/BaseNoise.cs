using Obsidian.API._Interfaces;
using SharpNoise;

namespace Obsidian.API.World.Generator.Noise;
internal partial class BaseNoise : INoise
{
    public string Type => "minecraft:base_noise";

    public required List<double> amplitudes;

    public required double firstOctave;

    public required int seed;

    public double GetValue(double x, double y, double z)
    {
        int octaves = amplitudes.Count;
        double result = 0.0;
        for (int i = 0; i < octaves; i++)
        {
            int s = (seed + i) & 0x7FFFFFFF;
            double noise1 = NoiseGenerator.GradientCoherentNoise3D(x, y, z, s, NoiseQuality.Standard);
            double noise2 = NoiseGenerator.GradientCoherentNoise3D(x, y, z, s+octaves, NoiseQuality.Standard);
            double noise = noise1 + noise2 / 2.0D;
            double persistence = amplitudes[i] * Math.Pow(2, octaves - i - 1) / (Math.Pow(2, octaves) - 1);
            result += noise * persistence;
            double lacunarity = Math.Pow(2, -firstOctave + i);
            x *= lacunarity;
            y *= lacunarity;
            z *= lacunarity;
        }

        return 10 * result / (3 * (1 + (1 / (octaves - 2))));
    }
}
