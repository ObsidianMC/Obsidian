using Obsidian.API.Noise;
using SharpNoise.Modules;
using System.Threading;

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

    // Chunks generate in parallel, so the noise lists only grow under this lock, and lookups read a snapshot of them.
    private readonly Lock noiseLock = new();
    private Perlin[] oreSnapshot = [];
    private Perlin[] stoneSnapshot = [];

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

        this.oreSnapshot = [.. OreNoises];
        this.stoneSnapshot = [.. StoneNoises];

        RiverNoise = new Cache
        {
            Source0 = new RiverSelector
            {
                RiverNoise = new Perlin
                {
                    Frequency = 0.0015,
                    Quality = SharpNoise.NoiseQuality.Standard,
                    Seed = seed + 1,
                    OctaveCount = 2,
                    Lacunarity = 10,
                    Persistence = 0.05
                }
            }
        };

        TempNoise = new Cache()
        {
            Source0 = new Clamp()
            {
                Source0 = new Perlin()
                {
                    Frequency = 0.0002,
                    Quality = SharpNoise.NoiseQuality.Standard,
                    Seed = seed + 2,
                    OctaveCount = 2,
                    // 1st octave smooth and low freq
                    // 2nd octave low powered by high freq multi
                    // to add ripples
                    Lacunarity = 120,
                    Persistence = 0.01
                }

            }
        };

        HumidityNoise = new Cache()
        {
            Source0 = new Clamp()
            {
                Source0 = new Perlin()
                {
                    Frequency = 0.00023,
                    Quality = SharpNoise.NoiseQuality.Standard,
                    Seed = seed + 3,
                    OctaveCount = 2,
                    // 1st octave smooth and low freq
                    // 2nd octave low powered by high freq multi
                    // to add ripples
                    Lacunarity = 120,
                    Persistence = 0.01
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
                    Bias = 0.08, // Favor more land than ocean
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
                Source0 = new ScaleBias
                {
                    Scale = 1.0,
                    Bias = -0.1, // Favor smoother terrain
                    Source0 = new Perlin()
                    {
                        Frequency = 0.001,
                        Quality = SharpNoise.NoiseQuality.Fast,
                        Seed = seed + 6,
                        Lacunarity = 1.1
                    }
                }
            }
        };

        SquashNoise = new Cache
        {
            Source0 = new ScaleBias
            {
                Scale = 1.0,
                Bias = -0.1, // Favor smoother terrain
                Source0 = new Perlin()
                {
                    Frequency = 0.0005,
                    Quality = SharpNoise.NoiseQuality.Fast,
                    Seed = seed + 98,
                    Lacunarity = 1.5
                }
            }
        };

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

    public Module Ore(int index) => this.GetNoise(OreNoises, ref this.oreSnapshot, index);

    public Module Stone(int index) => this.GetNoise(StoneNoises, ref this.stoneSnapshot, index);

    /// <summary>
    /// The noise at <paramref name="index"/>, adding the missing ones after the first noise's settings.
    /// </summary>
    private Perlin GetNoise(List<Perlin> noises, ref Perlin[] snapshot, int index)
    {
        var current = Volatile.Read(ref snapshot);
        if (index < current.Length)
            return current[index];

        lock (this.noiseLock)
        {
            // The seeds follow the list's count as noises are added, as they always have, so existing worlds keep their
            // ores and stone variants.
            var noisesToAdd = index + 1 - noises.Count;
            for (int i = 0; i < noisesToAdd; i++)
            {
                noises.Add(new Perlin()
                {
                    Frequency = noises[0].Frequency,
                    OctaveCount = noises[0].OctaveCount,
                    Quality = noises[0].Quality,
                    Seed = noises[0].Seed + i + noises.Count
                });
            }

            Volatile.Write(ref snapshot, [.. noises]);
            return noises[index];
        }
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
