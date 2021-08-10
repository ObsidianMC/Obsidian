using Obsidian.API.Noise;
using Obsidian.WorldData.Generators.Overworld.Carvers;
using SharpNoise.Modules;
using Blend = Obsidian.API.Noise.Blend;
using System.Collections.Generic;
using static Obsidian.API.Noise.VoronoiBiomes;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Terrain
{
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

            Dictionary<BiomeNoiseValue, Module> biomeMap = new()
            {
                { BiomeNoiseValue.DeepOcean, deepocean.Result },
                { BiomeNoiseValue.DeepFrozenOcean, deepocean.Result },
                { BiomeNoiseValue.FrozenOcean, ocean.Result },
                { BiomeNoiseValue.DeepColdOcean, deepocean.Result },
                { BiomeNoiseValue.ColdOcean, ocean.Result },
                { BiomeNoiseValue.DeepLukewarmOcean, deepocean.Result },
                { BiomeNoiseValue.LukewarmOcean, ocean.Result },
                { BiomeNoiseValue.DeepWarmOcean, deepocean.Result },
                { BiomeNoiseValue.WarmOcean, ocean.Result },
                { BiomeNoiseValue.Beach, new Constant { ConstantValue = 0.025 } },
                { BiomeNoiseValue.River, rivers.Result },
                { BiomeNoiseValue.FrozenRiver, rivers.Result },
                { BiomeNoiseValue.Badlands, badlands.Result },
                { BiomeNoiseValue.Desert, plains.Result },
                { BiomeNoiseValue.Savanna, hills.Result},
                { BiomeNoiseValue.ShatteredSavanna, mountains.Result },
                { BiomeNoiseValue.SavannaPlateau, hills.Result },
                { BiomeNoiseValue.ShatteredSavannaPlateau, mountains.Result },
                { BiomeNoiseValue.Jungle, plains.Result },
                { BiomeNoiseValue.Plains, plains.Result },
                { BiomeNoiseValue.Swamp, plains.Result },
                { BiomeNoiseValue.DarkForest, plains.Result },
                { BiomeNoiseValue.ExtremeHills, mountains.Result },
                { BiomeNoiseValue.Forest, plains.Result },
                { BiomeNoiseValue.TallBirchForest, plains.Result },
                { BiomeNoiseValue.BirchForest, plains.Result },
                { BiomeNoiseValue.MountainMeadow, hills.Result },
                { BiomeNoiseValue.GiantTreeTaiga, hills.Result },
                { BiomeNoiseValue.GiantSpruceTaiga, hills.Result },
                { BiomeNoiseValue.Taiga, plains.Result },
                { BiomeNoiseValue.StoneShore, plains.Result },
                { BiomeNoiseValue.SnowyBeach, new Constant { ConstantValue = 0.025 } },
                { BiomeNoiseValue.SnowyTundra, plains.Result },
                { BiomeNoiseValue.SnowyTaiga, plains.Result },
                { BiomeNoiseValue.MountainGrove, mountains.Result },
                { BiomeNoiseValue.SnowySlopes, mountains.Result },
                { BiomeNoiseValue.LoftyPeaks, mountains.Result },
                { BiomeNoiseValue.SnowCappedPeaks, mountains.Result },
                { BiomeNoiseValue.MushroomFields, plains.Result },
            };

            FinalBiomes = VoronoiBiomeNoise.Instance.result;

            var biomeTransitionSel2 = new Cache
            {
                Source0 = new TransitionMap
                {
                    Distance = 5,
                    Source0 = FinalBiomes
                }
            };

            Module scaled = new Blend
            {
                Distance = 2,
                Source0 = new TerrainSelect
                {
                    BiomeSelector = FinalBiomes,
                    Control = biomeTransitionSel2,
                    TerrainModules = biomeMap,
                }
            };

            if (isUnitTest)
            {
                scaled = new ScaleBias
                {
                    Source0 = FinalBiomes,
                    Scale = 1 / 10.0
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

        public BiomeNoiseValue GetBiome(double x, double z, double y = 0)
        {
            return (BiomeNoiseValue)FinalBiomes.GetValue(x, y, z);
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
}
