using SharpNoise;
using SharpNoise.Modules;

namespace Obsidian.API.Noise;

public class MojangPerlin : Module
{
    /// <summary>
    /// Source to blur. Expensive ops should be cached.
    /// </summary>
    public List<double> amplitudes { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public int firstOctave { get; set; } = 1;

    public required int seed;

    /// <summary>
    /// ctor.
    /// </summary>
    public MojangPerlin() : base(0)
    {

    }

    public override double GetValue(double x, double y, double z)
    {
/*        x += 0.5;
        z += 0.5;*/
        int octaves = amplitudes.Count;
        int equiPower = Math.Abs(firstOctave + octaves);
        double result = 0.0;
/*
        for (int a = 0; a < 3; a++)
        {
            int s = (seed + a) & 0x7FFFFFFF;
            double noise1 = NoiseGenerator.GradientCoherentNoise3D(x, y, z, s);
            double noise2 = NoiseGenerator.GradientCoherentNoise3D(x, y, z, s + firstOctave);
            result += noise1 + noise2 / 2.0D;
        }
*/
        for (int i = 0; i < octaves; i++)
        {
            int s = (seed + i + equiPower) & 0x7FFFFFFF;
            double noise1 = NoiseGenerator.GradientCoherentNoise3D(x, y, z, s);
            double noise2 = NoiseGenerator.GradientCoherentNoise3D(x, y, z, s + firstOctave);
            double noise = noise1 + noise2 / 2.0D;
            double persistence = amplitudes[i] * Math.Pow(2, octaves - i - 1) / (Math.Pow(2, octaves) - 1);
            result += noise * persistence;
            double lacunarity = Math.Pow(2, firstOctave + i);
            x *= lacunarity;
            y *= lacunarity;
            z *= lacunarity;
        }

        return 10 * result / (3 * (1 + (1 / (firstOctave+2))));
    }
}
