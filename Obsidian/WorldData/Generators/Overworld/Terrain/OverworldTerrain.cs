using Obsidian.API.Noise;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;
using Obsidian.WorldData.Generators.Overworld.Carvers;
using SharpNoise.Modules;
using static Obsidian.API.Noise.VoronoiBiomes;
using Blend = Obsidian.API.Noise.Blend;

namespace Obsidian.WorldData.Generators.Overworld.Terrain;

public class OverworldTerrain
{
    public Module Result { get; set; }

    public readonly OverworldTerrainSettings settings;

    private readonly Module ocean, deepocean, badlands, plains, hills, mountains, rivers;

    private readonly Module cave;

    private Module FinalBiomes;

    public OverworldTerrain(bool isUnitTest = false)
    {
        settings = OverworldGenerator.GeneratorSettings;
        ocean = new OceanTerrain().Result;
        deepocean = new DeepOceanTerrain().Result;
        plains = new PlainsTerrain().Result;
        hills = new HillsTerrain().Result;
        badlands = new BadlandsTerrain().Result;
        mountains = new MountainsTerrain().Result;
        rivers = new RiverTerrain().Result;
        cave = new CavesCarver().Result;

        Dictionary<Biomes, Module> biomesTerrainMap = new()
        {
            { Biomes.Badlands, badlands },
            { Biomes.BambooJungle, plains },
            { Biomes.BasaltDeltas, plains },
            { Biomes.Beach, plains },
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
            { Biomes.Grove, hills },
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
            { Biomes.SnowyBeach, plains },
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

        var biomeTerrain = new TerrainSelect(FinalBiomes)
        {
            Control = new TransitionMap(FinalBiomes, 10),
            TerrainModules = biomesTerrainMap
        };

        Module blendPass1 = new Blend(biomeTerrain)
        {
            Distance = 5
        };

        Module blendPass2 = new Blend(blendPass1)
        {
            Distance = 2
        };

        var scaledWorld = new SplitScaleBias()
        {
            Source0 = blendPass2,
            Center = 0,
            AboveCenterScale = 256, // world height minus sea level
            BelowCenterScale = 128, // sea level + abs(world floor)
            Bias = 64 // sea level
        };

        Result = isUnitTest ? blendPass2 : scaledWorld;

    }

    internal BaseBiome GetBiome(double x, double z, double y = 0)
    {
        return (BaseBiome)FinalBiomes.GetValue(x, y, z);
    }

    public double GetValue(double x, double z, double y = 0)
    {
        return Result.GetValue(x, y, z);
    }

    public bool IsCave(double x, double y, double z)
    {
        var val = cave.GetValue(x, y, z);
        return val > -0.5;
    }
}
