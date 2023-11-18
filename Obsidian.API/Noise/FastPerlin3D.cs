using FastNoiseOO;
using FastNoiseOO.Generators;
using SharpNoise;
using SharpNoise.Modules;
using System.Collections.Concurrent;
using System.Numerics;

namespace Obsidian.API.Noise;

public class FastPerlin3D : SharpNoise.Modules.Perlin
{
    private readonly FastNoiseOO.Generators.Perlin prln;

    private ConcurrentDictionary<(int chunkX, int chunkZ), float[]> storage = new();

    public FastPerlin3D()
    {
        prln = new FastNoiseOO.Generators.Perlin();
    }

    public void Cleanup(int chunkX, int chunkZ)
    {
        storage.TryRemove((chunkX, chunkZ), out var _);
    }

    public override double GetValue(double x, double y, double z)
    {
        y += 64;
        var (chunkX, chunkZ) = ((int)x >> 4, (int)z >> 4);
        var (startX, startY, startZ) = (chunkX << 4, 0, chunkZ << 4);
        if (storage.TryGetValue((chunkX, chunkZ), out var arr))
        {
            return arr[(((int)z - startZ) << 9) + (((int)y - startY) << 4) + ((int)x - startX)];
        }

        float[] resultArray = new float[16 * 16 * 512];
        Array.Clear(resultArray, 0, resultArray.Length);

        storage[(chunkX, chunkZ)] = prln.GenUniformGrid3D(startX, startY, startZ, 16, 512, 16, (float)Frequency, Seed, out var minMax); ;

        return resultArray[(((int)z - startZ) << 9) + (((int)y - startY) << 4) + ((int)x - startX)];

        //return base.GetValue(x, y, z);
    }
}
