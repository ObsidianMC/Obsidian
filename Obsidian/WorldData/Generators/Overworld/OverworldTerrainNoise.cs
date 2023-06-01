using Obsidian.API.Noise;
using SharpNoise.Modules;
using static SharpNoise.Modules.Curve;

namespace Obsidian.WorldData.Generators.Overworld;

public class OverworldTerrainNoise
{
    public readonly int waterLevel = 64;

    public readonly Module heightNoise, squashNoise, humidityNoise, tempNoise, erosionNoise, riverNoise, peakValleyNoise;

    public readonly Module terrainSelector, biomeSelector;

    private readonly int seed;

    public OverworldTerrainNoise(int seed)
    {
        this.seed = seed + 765; // add offset

        peakValleyNoise = new Cache()
        {
            Source0 = new Clamp()
            {
                Source0 = new Perlin()
                {
                    Seed = seed,
                    Frequency = 0.02,
                    Lacunarity = 2.132,
                    Quality = SharpNoise.NoiseQuality.Fast,
                    OctaveCount = 3
                }
            }
        };

        humidityNoise = new Cache()
        {
            Source0 = new Clamp()
            {
                Source0 = new Blur()
                {
                    Source0 = new Perlin()
                    {
                        Frequency = 0.002,
                        Quality = SharpNoise.NoiseQuality.Fast,
                        Seed = seed + 3,
                        OctaveCount = 3,
                        Lacunarity = 1.5
                    }
                }
            }
        };

        riverNoise = new Cache
        {
            Source0 = new RiverSelector
            {
                RiverNoise = new Perlin
                {
                    Frequency = 0.001,
                    Quality = SharpNoise.NoiseQuality.Fast,
                    Seed = seed + 2,
                    OctaveCount = 3,
                    Lacunarity = 1.5
                }
            }
        };

        tempNoise = new Cache()
        {
            Source0 = new Clamp()
            {
                Source0 = new Blur()
                {
                    Source0 = new Perlin()
                    {
                        Frequency = 0.001,
                        Quality = SharpNoise.NoiseQuality.Fast,
                        Seed = seed + 2
                    }
                }
            }
        };

        heightNoise = new Cache()
        {
            Source0 = new ContinentSelector
            {
                TerrainNoise = new ScaleBias
                {
                    Scale = 1.0,
                    Bias = 0.08,
                    Source0 = new Perlin()
                    {
                        Frequency = 0.0013,
                        Quality = SharpNoise.NoiseQuality.Fast,
                        Seed = seed + 4
                    }
                }

            }
        };

        erosionNoise = new Cache()
        {
            Source0 = new Clamp()
            {
                Source0 = new Perlin()
                {
                    Frequency = 0.002,
                    Quality = SharpNoise.NoiseQuality.Fast,
                    Seed = seed + 5
                }
            }
        };

        squashNoise = new Cache
        {
            Source0 = new Perlin()
            {
                Frequency = 0.0005,
                Quality = SharpNoise.NoiseQuality.Fast,
                Seed = seed + 5
            }
        };

        terrainSelector = new OverworldTerrain(heightNoise, squashNoise, erosionNoise, riverNoise, peakValleyNoise)
        {
            Seed = seed,
            TerrainStretch = 15
        };

        biomeSelector = new BiomeSelector(tempNoise, humidityNoise, heightNoise, erosionNoise, riverNoise, peakValleyNoise);
    }

    public int GetTerrainHeight(int x, int z)
    {
        for (int y = 320; y > 64; y--)
        {
            if (IsTerrain(x, y, z))
            {
                return y;
            }
        }
        return 0;
    }

    public bool IsTerrain(int x, int y, int z)
    {
        return terrainSelector.GetValue(x, (y + 32) * 2, z) > 0;
    }

    public Module Cave => new ScalePoint()
    {
        XScale = 1.0D,
        ZScale = 1.0D,
        YScale = 2.0D,
        Source0 = new Perlin
        {
            Frequency = 0.023,
            Lacunarity = 1.9,
            OctaveCount = 2,
            Persistence = 0.9,
            Quality = SharpNoise.NoiseQuality.Fast,
            Seed = seed
        }
    };

    public Module Ore(int index) => new TranslatePoint
    {
        XTranslation = 0,
        YTranslation = index * 384,
        ZTranslation = 0,
        Source0 = new Perlin
        {
            Frequency = 0.203,
            Lacunarity = 1.3,
            OctaveCount = 1,
            Persistence = 0.9,
            Quality = SharpNoise.NoiseQuality.Fast,
            Seed = seed
        }
    };

    public Module Stone(int index) => new TranslatePoint
    {
        XTranslation = 0,
        YTranslation = index * 384,
        ZTranslation = 0,
        Source0 = new Perlin
        {
            Frequency = 0.093,
            Lacunarity = 1.3,
            OctaveCount = 1,
            Persistence = 0.9,
            Quality = SharpNoise.NoiseQuality.Fast,
            Seed = seed
        }
    };

    public Module Decoration => new Multiply
    {
        Source0 = new Checkerboard(),
        Source1 = new Perlin
        {
            Frequency = 1.14,
            Lacunarity = 2.222,
            Seed = seed + 3,
            OctaveCount = 3
        }
    };

    public Module Biome => new Cache
    {
        Source0 = biomeSelector
    };

    // Set a constant biome here for development
    // public Module Biome => new Constant() { ConstantValue = (int)API.Biome.BambooJungle };
}
