using Obsidian.API.Noise;
using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators.Overworld;

public sealed class OverworldTerrainNoise
{
    private readonly Module biomeSelector;

    private readonly int seed;

    public int WaterLevel { get; } = 64;

    public Module RiverNoise { get; }
    public Module ErosionNoise { get; }
    public Module TempNoise { get; }
    public Module HumidityNoise { get; }
    public Module SquashNoise { get; set; }
    public Module HeightNoise { get; }
    public Module PeakValleyNoise { get; }
    public Module TerrainSelector { get; }

    public List<Perlin> OreNoises { get; } = new();
    public List<Perlin> StoneNoises { get; } = new();

    public OverworldTerrainNoise(int seed)
    {
        this.seed = seed + 765; // add offset

        OreNoises.Add(new Perlin()
        {
            Frequency = 0.203,
            OctaveCount = 1,
            Quality = SharpNoise.NoiseQuality.Fast,
            Seed = seed + 100
        });

        StoneNoises.Add(new Perlin()
        {
            Frequency = 0.093,
            OctaveCount = 1,
            Quality = SharpNoise.NoiseQuality.Fast,
            Seed = seed + 200
        });

        PeakValleyNoise = new Cache()
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

        HumidityNoise = new Cache()
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

        RiverNoise = new Cache
        {
            Source0 = new RiverSelector
            {
                RiverNoise = new Perlin
                {
                    Frequency = 0.001,
                    Quality = SharpNoise.NoiseQuality.Fast,
                    Seed = seed + 1,
                    OctaveCount = 3,
                    Lacunarity = 1.5
                }
            }
        };

        TempNoise = new Cache()
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

        HeightNoise = new Cache()
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

        ErosionNoise = new Cache()
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

        SquashNoise = new Cache
        {
            Source0 = new Perlin()
            {
                Frequency = 0.0005,
                Quality = SharpNoise.NoiseQuality.Fast,
                Seed = seed + 6
            }
        };

        TerrainSelector = new OverworldTerrain(HeightNoise, SquashNoise, ErosionNoise, RiverNoise, PeakValleyNoise)
        {
            Seed = seed,
            TerrainStretch = 15
        };

        biomeSelector = new BiomeSelector(TempNoise, HumidityNoise, HeightNoise, ErosionNoise, RiverNoise, PeakValleyNoise);
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
        return TerrainSelector.GetValue(x, (y + 32) * 2, z) > 0;
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

    public Module Ore(int index)
    {
        var noisesToAdd = index + 1 - OreNoises.Count;
        for (int i = 0; i < noisesToAdd; i++)
        {
            OreNoises.Add(new Perlin()
            {
                Frequency = OreNoises[0].Frequency,
                OctaveCount = OreNoises[0].OctaveCount,
                Quality = OreNoises[0].Quality,
                Seed = OreNoises[0].Seed + i + OreNoises.Count
            });
        }

        return OreNoises[index];
    }

    public Module Stone(int index)
    {
        var noisesToAdd = index + 1 - StoneNoises.Count;
        for (int i = 0; i < noisesToAdd; i++)
        {
            StoneNoises.Add(new Perlin()
            {
                Frequency = StoneNoises[0].Frequency,
                OctaveCount = StoneNoises[0].OctaveCount,
                Quality = StoneNoises[0].Quality,
                Seed = StoneNoises[0].Seed + i + StoneNoises.Count
            });
        }

        return StoneNoises[index];
    }

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
