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

    private readonly BaseTerrain ocean, deepocean, badlands, plains, hills, mountains, rivers;

    private readonly BaseCarver cave;

    private Module FinalBiomes;

    public OverworldTerrain(bool isUnitTest = false)
    {
        settings = OverworldGenerator.GeneratorSettings;
        ocean = new OceanTerrain();
        deepocean = new DeepOceanTerrain();
        plains = new PlainsTerrain();
        hills = new HillsTerrain();
        badlands = new BadlandsTerrain();
        mountains = new MountainsTerrain();
        rivers = new RiverTerrain();
        cave = new CavesCarver();

        Dictionary<Biomes, Module> biomesTerrainMap = new()
        {
            { Biomes.Badlands, badlands.Result },
            { Biomes.BambooJungle, plains.Result },
            { Biomes.BasaltDeltas, plains.Result },
            { Biomes.Beach, plains.Result },
            { Biomes.BirchForest, plains.Result },
            { Biomes.ColdOcean, ocean.Result },
            { Biomes.CrimsonForest, plains.Result },
            { Biomes.DarkForest, plains.Result },
            { Biomes.DeepColdOcean, deepocean.Result },
            { Biomes.DeepFrozenOcean, deepocean.Result },
            { Biomes.DeepLukewarmOcean, deepocean.Result },
            { Biomes.DeepOcean, deepocean.Result },
            { Biomes.Desert, plains.Result },
            { Biomes.DripstoneCaves, plains.Result },
            { Biomes.EndBarrens, plains.Result },
            { Biomes.EndHighlands, plains.Result },
            { Biomes.EndMidlands, plains.Result },
            { Biomes.ErodedBadlands, badlands.Result },
            { Biomes.FlowerForest, plains.Result },
            { Biomes.Forest, plains.Result },
            { Biomes.FrozenOcean, ocean.Result },
            { Biomes.FrozenPeaks, mountains.Result },
            { Biomes.FrozenRiver, rivers.Result },
            { Biomes.Grove, hills.Result },
            { Biomes.IceSpikes, badlands.Result },
            { Biomes.JaggedPeaks, mountains.Result },
            { Biomes.Jungle, hills.Result },
            { Biomes.LukewarmOcean, ocean.Result },
            { Biomes.LushCaves, plains.Result },
            { Biomes.Meadow, plains.Result },
            { Biomes.MushroomFields, plains.Result},
            { Biomes.NetherWastes, plains.Result },
            { Biomes.Ocean, ocean.Result },
            { Biomes.OldGrowthBirchForest, plains.Result },
            { Biomes.OldGrowthPineTaiga, plains.Result },
            { Biomes.OldGrowthSpruceTaiga, plains.Result },
            { Biomes.Plains, plains.Result },
            { Biomes.River, rivers.Result },
            { Biomes.Savanna, plains.Result },
            { Biomes.SavannaPlateau, hills.Result },
            { Biomes.SmallEndIslands, plains.Result },
            { Biomes.SnowyBeach, plains.Result },
            { Biomes.SnowyPlains, plains.Result },
            { Biomes.SnowySlopes, hills.Result },
            { Biomes.SnowyTaiga, plains.Result },
            { Biomes.SoulSandValley, plains.Result },
            { Biomes.SparseJungle, plains.Result },
            { Biomes.StonyPeaks, mountains.Result },
            { Biomes.StonyShore, plains.Result },
            { Biomes.SunflowerPlains, plains.Result },
            { Biomes.Swamp, badlands.Result },
            { Biomes.Taiga, hills.Result },
            { Biomes.TheEnd, plains.Result },
            { Biomes.TheVoid, plains.Result },
            { Biomes.WarmOcean, ocean.Result },
            { Biomes.WarpedForest, plains.Result },
            { Biomes.WindsweptForest, plains.Result },
            { Biomes.WindsweptGravellyHills, hills.Result },
            { Biomes.WindsweptHills, hills.Result },
            { Biomes.WindsweptSavanna, plains.Result },
            { Biomes.WoodedBadlands, badlands.Result }
        };

        FinalBiomes = VoronoiBiomeNoise.Instance.result;

        var biomeTransitionSel2 = new Cache
        {
            Source0 = new TransitionMap(FinalBiomes, 5)
        };

        Module scaled = new Blend(
            new TerrainSelect(FinalBiomes)
            {
                Control = biomeTransitionSel2,
                TerrainModules = biomesTerrainMap
            })
        {
            Distance = 2
        };

        if (isUnitTest)
        {
            scaled = new ScaleBias
            {
                Source0 = FinalBiomes,
                Scale = 1 / 85.0,
                //Bias = -1
            };
        }

        // Scale bias scales the verical output (usually -1.0 to +1.0) to
        // Minecraft values. If MinElev is 40 (leaving room for caves under oceans)
        // and MaxElev is 168, a value of -1 becomes 40, and a value of 1 becomes 168.
        var biased = new ScaleBias
        {
            Scale = (settings.MaxElev - settings.MinElev) / 2.0,
            Bias = settings.MinElev + ((settings.MaxElev - settings.MinElev) / 2.0),
            Source0 = scaled
        };

        Result = isUnitTest ? scaled : biased;

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
        var val = cave.Result.GetValue(x, y, z);
        return val > -0.5;
    }
}
