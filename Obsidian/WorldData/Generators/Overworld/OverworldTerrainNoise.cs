using Obsidian.API.Noise;
using Obsidian.WorldData.Generators.Overworld.Terrain;
using Org.BouncyCastle.Crypto.Engines;
using SharpNoise.Modules;
using static SharpNoise.Modules.Curve;
using Blend = Obsidian.API.Noise.Blend;

namespace Obsidian.WorldData.Generators.Overworld;

public class OverworldTerrainNoise
{
    public Module Heightmap { get; set; }

    public OverworldTerrainSettings Settings { get; private set; } = new OverworldTerrainSettings();

    public readonly Module terrainPerlin;

    public readonly Module ocean, deepocean, badlands, plains, hills, mountains, rivers;

    public readonly Module tunnels;

    public readonly Module transitions, biomeTerrain, blendPass1, selectiveBlend;

    private readonly int seed;

    private bool isUnitTest;

    public OverworldTerrainNoise(int seed, bool isUnitTest = false)
    {
        this.isUnitTest = isUnitTest;
        this.seed = seed + 765; // add offset
        terrainPerlin = new Turbulence()
        {
            Frequency = 0.1234,
            Power = 2,
            Roughness = 3,
            Seed = seed + 1,
            Source0 = new Perlin()
            {
                Frequency = 0.0356,
                OctaveCount = 3,
                Lacunarity = 0.8899,
                Persistence = 0.1334,
                Quality = SharpNoise.NoiseQuality.Fast,
                Seed = seed
            }
        };

        ocean = new OceanTerrain(seed, Settings);
        deepocean = new DeepOceanTerrain(seed, Settings);
        plains = new PlainsTerrain(seed, Settings);
        hills = new HillsTerrain(seed, Settings);
        badlands = new BadlandsTerrain(seed, Settings);
        mountains = new MountainsTerrain(seed, Settings);
        rivers = new RiverTerrain(seed, Settings);

        tunnels = new VoronoiTunnels()
        {
            Frequency = 0.0123456,
            Seed = this.seed
        };

        Dictionary<Biomes, Module> biomesTerrainMap = new()
        {
            { Biomes.Badlands, badlands },
            { Biomes.BambooJungle, plains },
            { Biomes.BasaltDeltas, plains },
            { Biomes.Beach, new Constant() { ConstantValue = 0 } },
            { Biomes.BirchForest, plains },
            { Biomes.ColdOcean, ocean },
            { Biomes.CrimsonForest, plains },
            { Biomes.DarkForest, plains },
            { Biomes.DeepColdOcean, deepocean },
            { Biomes.DeepFrozenOcean, deepocean },
            { Biomes.DeepLukewarmOcean, deepocean },
            { Biomes.DeepOcean, deepocean },
            { Biomes.Desert, plains },
            { Biomes.DripstoneCaves, plains },
            { Biomes.EndBarrens, plains },
            { Biomes.EndHighlands, plains },
            { Biomes.EndMidlands, plains },
            { Biomes.ErodedBadlands, badlands },
            { Biomes.FlowerForest, plains },
            { Biomes.Forest, plains },
            { Biomes.FrozenOcean, ocean },
            { Biomes.FrozenPeaks, mountains },
            { Biomes.FrozenRiver, rivers },
            { Biomes.Grove, plains },
            { Biomes.IceSpikes, badlands },
            { Biomes.JaggedPeaks, mountains },
            { Biomes.Jungle, plains },
            { Biomes.LukewarmOcean, ocean },
            { Biomes.LushCaves, plains },
            { Biomes.Meadow, plains },
            { Biomes.MushroomFields, plains },
            { Biomes.NetherWastes, plains },
            { Biomes.Ocean, ocean },
            { Biomes.OldGrowthBirchForest, plains },
            { Biomes.OldGrowthPineTaiga, plains },
            { Biomes.OldGrowthSpruceTaiga, plains },
            { Biomes.Plains, plains },
            { Biomes.River, rivers },
            { Biomes.Savanna, plains },
            { Biomes.SavannaPlateau, hills },
            { Biomes.SmallEndIslands, plains },
            { Biomes.SnowyBeach, new Constant() { ConstantValue = 0 } },
            { Biomes.SnowyPlains, plains },
            { Biomes.SnowySlopes, hills },
            { Biomes.SnowyTaiga, plains },
            { Biomes.SoulSandValley, plains },
            { Biomes.SparseJungle, plains },
            { Biomes.StonyPeaks, mountains },
            { Biomes.StonyShore, plains },
            { Biomes.SunflowerPlains, plains },
            { Biomes.Swamp, badlands },
            { Biomes.Taiga, hills },
            { Biomes.TheEnd, plains },
            { Biomes.TheVoid, plains },
            { Biomes.WarmOcean, ocean },
            { Biomes.WarpedForest, plains },
            { Biomes.WindsweptForest, plains },
            { Biomes.WindsweptGravellyHills, hills },
            { Biomes.WindsweptHills, hills },
            { Biomes.WindsweptSavanna, plains },
            { Biomes.WoodedBadlands, badlands }
        };

        transitions = new Blend(new TransitionMap(Biome, 9))
        {
            Distance = 3
        };

        biomeTerrain = new TerrainSelect(Biome)
        {
            Control = transitions,
            TerrainModules = biomesTerrainMap
        };


        blendPass1 = new Blend(biomeTerrain)
        {
            Distance = 2
        };

        // Only blend on transitions for performance
        selectiveBlend = new Select()
        {
            Source0 = biomeTerrain,
            Source1 = blendPass1,
            Control = transitions,
            LowerBound = -0.9,
            UpperBound = 1.0,
            EdgeFalloff = 0
        };

        var scaledWorld = new SplitScaleBias()
        {
            Source0 = selectiveBlend,
            Center = 0,
            AboveCenterScale = 256, // world height minus sea level
            BelowCenterScale = 128, // sea level + abs(world floor)
            Bias = 64 // sea level
        };

        Heightmap = isUnitTest ? blendPass1 : scaledWorld;
    }

    public Module TemperaturePerlin => new Perlin()
    {
        Frequency = 0.0042,
        Quality = SharpNoise.NoiseQuality.Fast,
        Seed = seed + 2
    };

    public Module HumidityPerlin => new Perlin()
    {
        Frequency = 0.0042,
        Quality = SharpNoise.NoiseQuality.Fast,
        Seed = seed + 3
    };

    public Module HeightPerlin => new Perlin()
    {
        Frequency = 0.0042,
        Quality = SharpNoise.NoiseQuality.Fast,
        Seed = seed + 4
    };

    public bool IsTerrain(int x, int y, int z)
    {
        var terrainAmplification = 1d;
        double bias = y - 128; // put half world height to 0;
        bias = Math.Pow(bias, 3)/terrainAmplification;
        bias /= 192d; // world height to -1 < y < 1;
        double n = terrainPerlin.GetValue(x, y, z);
        return n - bias > 0;
    }

    public Module Cave => new Turbulence()
    {
        Frequency = 0.1234,
        Power = 1,
        Roughness = 3,
        Seed = seed + 1,
        Source0 = new Max()
        {
            Source0 = tunnels,
            Source1 = new ScalePoint
            {
                XScale = 1 / 1024.0,
                YScale = 1 / 384.0,
                ZScale = 1 / 1024.0,
                Source0 = new Curve
                {
                    ControlPoints = new List<ControlPoint>()
                {
                     new Curve.ControlPoint(-1, -1),
                     new Curve.ControlPoint(-0.7, -0.5),
                     new Curve.ControlPoint(-0.4, -0.5),
                     new Curve.ControlPoint(1, 1),
                },
                    Source0 = new Billow
                    {
                        Frequency = 18.12345,
                        Seed = seed + 2,
                        Quality = SharpNoise.NoiseQuality.Fast,
                        OctaveCount = 6,
                        Lacunarity = 1.2234,
                        Persistence = 1.23
                    }
                }
            }
        }
    };

    public Module Decoration => new Multiply
    {
        Source0 = new Checkerboard(),
        Source1 = new Perlin
        {
            Frequency = 1.14,
            Lacunarity = 2.222,
            Seed = seed + 3
        }
    };

    public Module Biome => new Cache
    {
        Source0 = new Turbulence
        {
            Frequency = 0.007119,
            Power = 16,
            Roughness = 3,
            Seed = seed + 4,
            Source0 = new VoronoiBiomes(TemperaturePerlin, HumidityPerlin, HeightPerlin, isUnitTest)
            {
                Frequency = 0.0054159,
                Seed = seed + 5
            }
        }
    };

    // Set a constant biome here for development
    //public Module Biome => new Constant() { ConstantValue = (int)Biomes.Forest };
}
