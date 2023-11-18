using FastNoiseOO;
using FastNoiseOO.Generators;
using SharpNoise;
using SharpNoise.Modules;
using System.Collections.Concurrent;
using System.Numerics;

namespace Obsidian.API.Noise;

public class FastPerlin : SharpNoise.Modules.Perlin
{
    private readonly FastNoiseOO.Generators.Perlin prln;

    private ConcurrentDictionary<(int chunkX, int chunkZ), ConcurrentDictionary<double, float[]>> storage2 = new();

    public FastPerlin()
    {
        prln = new FastNoiseOO.Generators.Perlin();
    }

    public void Cleanup(int chunkX, int chunkZ)
    {
        storage2.TryRemove((chunkX, chunkZ), out var _);
    }

    public override double GetValue(double x, double y, double z)
    {
        var (chunkX, chunkZ) = ((int)x >> 4, (int)z >> 4);
        var (startX, startZ) = (chunkX << 4, chunkZ << 4);
        if (storage2.TryGetValue((chunkX, chunkZ), out var yDict))
        {
            if (yDict.TryGetValue(y, out var arr))
            {
                return arr[(((int)x - startX) << 4) + ((int)z - startZ)];
            }
        } 
        else
        {
            storage2[(chunkX, chunkZ)] = new();
        }

        // Get all of the X/Z values for this Y
        float lacunarityFactor = 1.0f;
        float persistenceFactor = 1.0f;
        float[] resultArray = new float[16 * 16];
        Array.Clear(resultArray, 0, resultArray.Length);
        for (int o = 0; o < OctaveCount; o++)
        {
            int seed = (Seed + o) & 0x7FFFFFFF;
            var r = prln.GenUniformGrid2D(startX, startZ, 16, 16, (float)Frequency * lacunarityFactor, seed, out var minMax);
            for (int i = 0; i < 16*16; i++)
            {
                resultArray[i] += r[i] * persistenceFactor;
            }
            lacunarityFactor *= (float)Lacunarity;
            persistenceFactor *= (float)Persistence;
        }
        storage2[(chunkX, chunkZ)][y] = resultArray;

        return resultArray[(((int)x - startX) << 4) + ((int)z - startZ)];


        /*        double num = 0.0;
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
        */
        //return base.GetValue(x, y, z);
    }
}
