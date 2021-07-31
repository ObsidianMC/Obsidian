using Obsidian.API.Noise;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;
using Obsidian.WorldData.Generators.Overworld.Carvers;
using static Obsidian.Utilities.NumericsHelper;
using SharpNoise;
using SharpNoise.Modules;
using System;
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

        private readonly BaseBiomeNoise humidity, temperature, terrain;

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

            humidity = new HumidityNoise(ots);
            temperature = new TemperatureNoise(ots);
            terrain = new TerrainNoise(ots);

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

            // Scale Point multiplies input values by the scaling factors.
            // Used to stretch or shrink the terrain horizontally.
            //var scaled = GetScaledModuleOutput(MergedLandOceanRivers());
            //var scaled = GetScaledModuleOutput(LandOceanSelector());
            //var scaled = GetScaledModuleOutput(temperature.RiverSelector);
            /*
                        var landLockedFix = new Func<Module, double, double, double, double>((Module source0, double x, double y, double z) =>
                        {
                            double center = source0.GetValue(x, y, z);
                            if (center > 0) { return center; } // Only applies to ocean
                            int neighborOceanAreas = 0;
                            neighborOceanAreas += source0.GetValue(x - 1, y, z) < 0 ? 1 : 0;
                            neighborOceanAreas += source0.GetValue(x + 1, y, z) < 0 ? 1 : 0;
                            neighborOceanAreas += source0.GetValue(x + 1, y, z - 1) < 0 ? 1 : 0;
                            neighborOceanAreas += source0.GetValue(x + 1, y, z + 1) < 0 ? 1 : 0;
                            neighborOceanAreas += source0.GetValue(x, y, z - 1) < 0 ? 1 : 0;
                            neighborOceanAreas += source0.GetValue(x, y, z + 1) < 0 ? 1 : 0;
                            neighborOceanAreas += source0.GetValue(x - 1, y, z + 1) < 0 ? 1 : 0;
                            neighborOceanAreas += source0.GetValue(x + 1, y, z + 1) < 0 ? 1 : 0;

                            return neighborOceanAreas < 6 ? Math.Abs(center) : center;

                            if (neighborOceanAreas < 2)
                            {
                                return 1;
                            }
                            else
                            {
                                return -1;
                            }

                            // If all neighbors are land, become land.
                            if (source0.GetValue(x - 1, y, z) > 0 &&
                                source0.GetValue(x + 1, y, z) > 0 &&
                                source0.GetValue(x, y, z - 1) > 0 &&
                                source0.GetValue(x, y, z + 1) > 0)
                            {
                                return 1;
                            }
                            // If the only land is 

                            return source0.GetValue(x - 1, y, z) > 0 &&
                                source0.GetValue(x + 1, y, z) > 0 &&
                                source0.GetValue(x, y, z - 1) > 0 &&
                                source0.GetValue(x, y, z + 1) > 0 ? 1 : source0.GetValue(x, y, z);
                        });
            */

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

            /*var scaled = new ScaleBias
            {
                Source0 = biomeTransitionSelector,
                Scale = 1/28.0
            };*/

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

            //var scaled = plains.Result;

            //var scaled = biomeTransitionSel2;

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

        public bool IsRiver(double x, double z, double y = 0)
        {
            return GetScaledModuleOutput(MergedRivers()).GetValue(x, y, z) > 0.5;
        }

        public bool IsMountain(double x, double z, double y = 0)
        {
            return GetScaledModuleOutput(terrain.BiomeSelector).GetValue(x, y, z) > 0.75;
        }

        public bool IsHills(double x, double z, double y = 0)
        {
            var val = GetScaledModuleOutput(terrain.BiomeSelector).GetValue(x, y, z);
            return val >= 0.25 && val <= 0.75;
        }

        public bool IsPlains(double x, double z, double y = 0)
        {
            return GetScaledModuleOutput(terrain.BiomeSelector).GetValue(x, y, z) < 0.25;
        }

        public bool IsOcean(double x, double z, double y = 0)
        {
            return GetScaledModuleOutput(LandOceanSelector()).GetValue(x, y, z) < 0.5;
        }

        public bool IsCave(double x, double y, double z)
        {
            var val = cave.Result.GetValue(x, y, z);
            return val > -0.5;
        }

        private Module LandOceanSelector()
        {
            return new Cache
            {
                Source0 = new Clamp
                {
                    LowerBound = 0.0,
                    UpperBound = 1.0,
                    Source0 = new ScalePoint
                    {
                        // Scale horizontally to make Continents larger/smaller
                        XScale = 1 / (settings.ContinentSize * 1.01),
                        ZScale = 1 / (settings.ContinentSize * 1.01),
                        // Values are now Ocean < 0.5 < Land
                        Source0 = new ScaleBias
                        {
                            Bias = 0.5 + settings.OceanLandRatio,
                            Scale = 0.5,
                            // Actual noise for continents. Ocean < 0 < Land
                            Source0 = new Perlin
                            {
                                Seed = settings.Seed,
                                Frequency = 0.125,
                                Persistence = 0.5,
                                Lacunarity = 1.508984375,
                                OctaveCount = 3,
                                Quality = NoiseQuality.Best,
                            }
                        }
                    }
                }
            };
        }

        private Module MergedLandOcean()
        {
            return new Cache
            {
                Source0 = new Select
                {
                    Source0 = ocean.Result,
                    Source1 = TerrainSelect(),
                    Control = LandOceanSelector(),
                    EdgeFalloff = 0.05,
                    LowerBound = 0.49,
                    UpperBound = 2.0
                }
            };
        }

        private Module MergedLandOceanRivers()
        {
            return new Cache
            {
                Source0 = new Select
                {
                    Source0 = MergedLandOcean(),
                    Source1 = rivers.Result,
                    Control = MergedRivers(),
                    LowerBound = 0.5,
                    UpperBound = 2.0,
                    EdgeFalloff = 0.4
                }
            };
        }

        /// <summary>
        /// Determine whether to return plains, hills or mountains
        /// and blend them into each other
        /// </summary>
        /// <returns>A Cached result.</returns>
        private Module TerrainSelect()
        {
            return new Cache
            {
                Source0 = new Select
                {
                    Source1 = mountains.Result,
                    Source0 = new Select
                    {
                        Source0 = hills.Result,
                        Source1 = plains.Result,
                        Control = terrain.BiomeSelector,
                        LowerBound = -2.0,
                        UpperBound = -0,
                        EdgeFalloff = 0.1
                    },
                    Control = terrain.BiomeSelector,
                    LowerBound = 0.66,
                    UpperBound = 2.0,
                    EdgeFalloff = 0.1
                }
            };
        }

        private Module MergedRivers()
        {
            return new Cache
            {
                // Use Select to isolate rivers to only land masses
                Source0 = new Select
                {
                    Source0 = new Constant { ConstantValue = 0 },
                    // Just add em all together then clamp it
                    Source1 = new Clamp
                    {
                        UpperBound = 1.0,
                        LowerBound = 0.0,
                        Source0 = new Max
                        {
                            Source0 = humidity.RiverSelector,
                            Source1 = new Max
                            {
                                Source0 = temperature.RiverSelector,
                                Source1 = terrain.RiverSelector
                            }
                        }
                    },
                    Control = LandOceanSelector(),
                    EdgeFalloff = 0.15,
                    LowerBound = 0.65,
                    UpperBound = 2.0
                }
            };
        }

        private Module GetScaledModuleOutput(Module input)
        {
            return new ScalePoint
            {
                XScale = 1,
                YScale = 1,
                ZScale = 1,
                Source0 = input
            };
        }
    }
}
