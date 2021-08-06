using Obsidian.API.Noise;
using Obsidian.WorldData.Generators.Overworld.Carvers;
using SharpNoise.Modules;
using Blend = Obsidian.API.Noise.Blend;
using System.Collections.Generic;
using static Obsidian.API.Noise.VoronoiBiomes;

namespace Obsidian.WorldData.Generators.Overworld.Terrain
{
    public class OverworldTerrain
    {
        public Module Result { get; set; }

        public readonly OverworldTerrainSettings settings;

        private readonly BaseTerrain ocean, badlands, plains, hills, mountains, rivers;

        private readonly BaseCarver cave;

        private Module FinalBiomes;

        public OverworldTerrain(OverworldTerrainSettings ots, bool isUnitTest = false)
        {
            settings = ots;

            ocean = new OceanTerrain(ots);
            plains = new PlainsTerrain(ots);
            hills = new HillsTerrain(ots);
            badlands = new BadlandsTerrain(ots);
            mountains = new MountainsTerrain(ots);
            rivers = new RiverTerrain(ots);
            cave = new CavesCarver(ots);

            Dictionary<BiomeNoiseValue, Module> biomeMap = new()
            {
                { BiomeNoiseValue.DeepOcean, ocean.Result },
                { BiomeNoiseValue.DeepFrozenOcean, ocean.Result },
                { BiomeNoiseValue.FrozenOcean, ocean.Result },
                { BiomeNoiseValue.DeepColdOcean, ocean.Result },
                { BiomeNoiseValue.ColdOcean, ocean.Result },
                { BiomeNoiseValue.DeepLukewarmOcean, ocean.Result },
                { BiomeNoiseValue.LukewarmOcean, ocean.Result },
                { BiomeNoiseValue.DeepWarmOcean, ocean.Result },
                { BiomeNoiseValue.WarmOcean, ocean.Result },
                { BiomeNoiseValue.Beach, new Constant { ConstantValue = 0.025 } },
                { BiomeNoiseValue.River, rivers.Result },
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
                { BiomeNoiseValue.SnowyBeach, plains.Result },
                { BiomeNoiseValue.SnowyTundra, plains.Result },
                { BiomeNoiseValue.SnowyTaiga, plains.Result },
                { BiomeNoiseValue.MountainGrove, mountains.Result },
                { BiomeNoiseValue.SnowySlopes, mountains.Result },
                { BiomeNoiseValue.LoftyPeaks, mountains.Result },
                { BiomeNoiseValue.SnowCappedPeaks, mountains.Result },
                { BiomeNoiseValue.MushroomFields, plains.Result },
            };

            var pass1 = new Cache
            {
                Source0 = new Turbulence
                {
                    Frequency = 0.007519,
                    Power = 16,
                    Roughness = 3,
                    Seed = ots.Seed + 123,
                    Source0 = new BitShiftInput
                    {
                        Amount = 2,
                        Left = false,
                        Source0 = new VoronoiBiomes
                        {
                            BorderSize = 0.07,
                            Frequency = 0.014159,
                            Seed = ots.Seed
                        }
                    }
                }
            };

            var addMooshroom = new Select
            {
                Control = pass1,
                LowerBound = (double)VoronoiBiomes.BiomeNoiseValue.WarmOcean - 0.01,
                UpperBound = (double)VoronoiBiomes.BiomeNoiseValue.WarmOcean + 0.01,
                Source0 = pass1,
                Source1 = new Select
                {
                    LowerBound = 0.95, // They're rare
                    UpperBound = 0.96,
                    Source0 = new Constant { ConstantValue = (double)VoronoiBiomes.BiomeNoiseValue.WarmOcean },
                    Source1 = new Constant { ConstantValue = (double)VoronoiBiomes.BiomeNoiseValue.MushroomFields },
                    Control = new Turbulence
                    {
                        Seed = ots.Seed + 11,
                        Power = 11,
                        Roughness = 2,
                        Frequency = 0.0564,
                        Source0 = new Cell
                        {
                            Seed = ots.Seed + 9,
                            Frequency = 0.0102345,
                            Type = Cell.CellType.Voronoi
                        },
                    }
                }
            };
            
            FinalBiomes = new Cache
            {
                Source0 = addMooshroom
            };

            var biomeTransitionSel2 = new Cache
            {
                Source0 = new TransitionMap
                {
                    Distance = 5,
                    Source0 = FinalBiomes
                }
            };

            var scaled = new Blend
            {
                Distance = 2,
                Source0 = new TerrainSelect
                {
                    BiomeSelector = FinalBiomes,
                    Control = biomeTransitionSel2,
                    TerrainModules = biomeMap,
                }
            };

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

        public VoronoiBiomes.BiomeNoiseValue GetBiome(double x, double z, double y = 0)
        {
            return (VoronoiBiomes.BiomeNoiseValue)FinalBiomes.GetValue(x, y, z);
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
