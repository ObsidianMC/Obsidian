using Obsidian.API.Noise;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;
using Obsidian.WorldData.Generators.Overworld.Carvers;
using Obsidian.WorldData.Generators.Overworld.Terrain;
using SharpNoise.Modules;
using static Obsidian.API.Noise.VoronoiBiomes;
using Blend = Obsidian.API.Noise.Blend;

namespace Obsidian.WorldData.Generators.Overworld;

public class OverworldTerrain : Module
{
    public Module result;

    public readonly OverworldTerrainSettings settings;

    public readonly Module ocean, deepocean, badlands, plains, hills, mountains, rivers;

    public readonly Module caves, tunnels;

    public readonly Module transitions, biomeTerrain, blendPass1, blendPass2, selectiveBlend;

    public Module FinalBiomes;

    public OverworldTerrain(bool isUnitTest = false) : base(0)
    {
        settings = OverworldGenerator.GeneratorSettings;
        ocean = new OceanTerrain();
        deepocean = new DeepOceanTerrain();
        plains = new PlainsTerrain();
        hills = new HillsTerrain();
        badlands = new BadlandsTerrain();
        mountains = new MountainsTerrain();
        rivers = new RiverTerrain();
        caves = new CavesCarver();
        tunnels = new VoronoiTunnels()
        {
            Frequency = 0.0123456,
            Seed = settings.Seed
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
            { Biomes.Jungle, hills },
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

        FinalBiomes = VoronoiBiomeNoise.Instance.result;

        // For debugging, we can override the biome here
        // which is usefull for developing a biome
        // FinalBiomes = new Constant() { ConstantValue = (int)Biomes.StonyPeaks };

        transitions = new Blend(new TransitionMap(FinalBiomes, 9))
        {
            Distance = 3
        };

        biomeTerrain = new Cache()
        {
            Source0 = new TerrainSelect(FinalBiomes)
            {
                Control = transitions,
                TerrainModules = biomesTerrainMap
            }
        };

        blendPass1 = new Blend(biomeTerrain)
        {
            Distance = 2
        };

        blendPass2 = new Blend(blendPass1)
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

        var mapScaledBiomes = new ScaleBias()
        {
            Scale = 1 / 30.0,
            Bias = -1,
            Source0 = FinalBiomes
        };

        result = isUnitTest ? FinalBiomes : scaledWorld;

    }

    internal BaseBiome GetBiome(double x, double z, double y = 0)
    {
        return (BaseBiome)FinalBiomes.GetValue(x, y, z);
    }

    public override double GetValue(double x, double z, double y = 0)
    {
        return result.GetValue(x, y, z);
    }

    public bool IsCave(double x, double y, double z)
    {
        var c = new Turbulence()
        {
            Frequency = 0.1234,
            Power = 1,
            Roughness = 3,
            Seed = settings.Seed,
            Source0 = new Max()
            {
                Source0 = tunnels,
                Source1 = caves
            }
        };

        return c.GetValue(x, y, z) > 0;
    }
}
