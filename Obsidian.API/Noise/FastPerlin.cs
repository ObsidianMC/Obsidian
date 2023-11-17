using FastNoiseOO;
using FastNoiseOO.Generators;
using SharpNoise;
using SharpNoise.Modules;

namespace Obsidian.API.Noise;

public class FastPerlin : SharpNoise.Modules.Perlin
{
    private readonly FastNoiseOO.Generators.Perlin prln;
    public FastPerlin()
    {
        prln = new FastNoiseOO.Generators.Perlin();
    }

    public override double GetValue(double x, double y, double z)
    {
        double num = 0.0;
        double num2 = 1.0;
        x *= Frequency;
        y *= Frequency;
        z *= Frequency;
        for (int i = 0; i < OctaveCount; i++)
        {
            int seed = (Seed + i) & 0x7FFFFFFF;
            double num3 = prln.GenSingle3D((float)x, (float)y, (float)z, seed);
            num += num3 * num2;
            x *= Lacunarity;
            y *= Lacunarity;
            z *= Lacunarity;
            num2 *= Persistence;
        }

        return num;

        //return base.GetValue(x, y, z);
    }
}
