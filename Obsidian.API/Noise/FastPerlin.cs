using FastNoiseOO;
using FastNoiseOO.Generators;
using SharpNoise.Modules;

namespace Obsidian.API.Noise;

public class FastPerlin : SharpNoise.Modules.Perlin
{
    public override double GetValue(double x, double y, double z)
    {
        var prln = new FastNoiseOO.Generators.Perlin();
        //prln.Set("Frequency", (float)Frequency);
        //prln.Set("Lacunarity", (float)Lacunarity);
        //prln.Set("OctaveCount", (float)OctaveCount);
        //prln.Set("Persistence", (float)Persistence);
        return prln.GenSingle3D((float)x, (float)y, (float)z, Seed);
    }
}
