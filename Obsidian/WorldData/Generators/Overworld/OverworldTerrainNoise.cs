using Obsidian.API.Noise;
using Obsidian.WorldData.Generators.Overworld.Terrain;
using SharpNoise.Modules;
using static SharpNoise.Modules.Curve;
using Blend = Obsidian.API.Noise.Blend;

namespace Obsidian.WorldData.Generators.Overworld;

public class OverworldTerrainNoise
{
    public Module Heightmap { get; set; }

    public OverworldTerrainSettings Settings { get; private set; } = new OverworldTerrainSettings();

    public readonly Module ocean, deepocean, badlands, plains, hills, mountains, rivers;

    public readonly Module tunnels;

    public readonly Module transitions, biomeTerrain, blendPass1, selectiveBlend;

    private readonly int seed;

    private bool isUnitTest;

    public OverworldTerrainNoise(int seed, bool isUnitTest = false)
    {
        this.isUnitTest = isUnitTest;
        this.seed = seed + 765; // add offset
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

        Dictionary<Biome, Module> biomesTerrainMap = new()
        {
            { API.Biome.Badlands, badlands },
            { API.Biome.BambooJungle, plains },
            { API.Biome.BasaltDeltas, plains },
            { API.Biome.Beach, new Constant() { ConstantValue = 0 } },
            { API.Biome.BirchForest, plains },
            { API.Biome.ColdOcean, ocean },
            { API.Biome.CrimsonForest, plains },
            { API.Biome.DarkForest, plains },
            { API.Biome.DeepColdOcean, deepocean },
            { API.Biome.DeepFrozenOcean, deepocean },
            { API.Biome.DeepLukewarmOcean, deepocean },
            { API.Biome.DeepOcean, deepocean },
            { API.Biome.Desert, plains },
            { API.Biome.DripstoneCaves, plains },
            { API.Biome.EndBarrens, plains },
            { API.Biome.EndHighlands, plains },
            { API.Biome.EndMidlands, plains },
            { API.Biome.ErodedBadlands, badlands },
            { API.Biome.FlowerForest, plains },
            { API.Biome.Forest, plains },
            { API.Biome.FrozenOcean, ocean },
            { API.Biome.FrozenPeaks, mountains },
            { API.Biome.FrozenRiver, rivers },
            { API.Biome.Grove, plains },
            { API.Biome.IceSpikes, badlands },
            { API.Biome.JaggedPeaks, mountains },
            { API.Biome.Jungle, plains },
            { API.Biome.LukewarmOcean, ocean },
            { API.Biome.LushCaves, plains },
            { API.Biome.Meadow, plains },
            { API.Biome.MushroomFields, plains },
            { API.Biome.NetherWastes, plains },
            { API.Biome.Ocean, ocean },
            { API.Biome.OldGrowthBirchForest, plains },
            { API.Biome.OldGrowthPineTaiga, plains },
            { API.Biome.OldGrowthSpruceTaiga, plains },
            { API.Biome.Plains, plains },
            { API.Biome.River, rivers },
            { API.Biome.Savanna, plains },
            { API.Biome.SavannaPlateau, hills },
            { API.Biome.SmallEndIslands, plains },
            { API.Biome.SnowyBeach, new Constant() { ConstantValue = 0 } },
            { API.Biome.SnowyPlains, plains },
            { API.Biome.SnowySlopes, hills },
            { API.Biome.SnowyTaiga, plains },
            { API.Biome.SoulSandValley, plains },
            { API.Biome.SparseJungle, plains },
            { API.Biome.StonyPeaks, mountains },
            { API.Biome.StonyShore, plains },
            { API.Biome.SunflowerPlains, plains },
            { API.Biome.Swamp, badlands },
            { API.Biome.Taiga, hills },
            { API.Biome.TheEnd, plains },
            { API.Biome.TheVoid, plains },
            { API.Biome.WarmOcean, ocean },
            { API.Biome.WarpedForest, plains },
            { API.Biome.WindsweptForest, plains },
            { API.Biome.WindsweptGravellyHills, hills },
            { API.Biome.WindsweptHills, hills },
            { API.Biome.WindsweptSavanna, plains },
            { API.Biome.WoodedBadlands, badlands }
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
            Source0 = new VoronoiBiomes(isUnitTest)
            {
                Frequency = 0.0054159,
                Seed = seed + 5
            }
        }
    };

    // Set a constant biome here for development
    //public Module Biome => new Constant() { ConstantValue = (int)Biomes.Jungle };
}
