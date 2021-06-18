using Obsidian.WorldData.Generators.Overworld.BiomeNoise;
using Obsidian.WorldData.Generators.Overworld.Carvers;
using SharpNoise;
using SharpNoise.Modules;
using System;
using System.Collections.Generic;

namespace Obsidian.WorldData.Generators.Overworld.Terrain
{
    public class OverworldTerrain
    {
        public Module Result { get; set; }

        public readonly OverworldTerrainSettings settings;

        private readonly BaseTerrain ocean, badlands, plains, hills, mountains, rivers;

        private readonly BaseBiomeNoise humidity, temperature, terrain;

        private readonly BaseCarver cave;

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

            // Scale Point multiplies input values by the scaling factors.
            // Used to stretch or shrink the terrain horizontally.
            var scaled = GetScaledModuleOutput(MergedLandOceanRivers());
            //var scaled = GetScaledModuleOutput(mountains.Result);

            // Scale bias scales the verical output (usually -1.0 to +1.0) to
            // Minecraft values. If MinElev is 40 (leaving room for caves under oceans)
            // and MaxElev is 168, a value of -1 becomes 40, and a value of 1 becomes 168.
            var biased = new ScaleBias
            {
                Scale = (settings.MaxElev - settings.MinElev) / 2.0,
                Bias = settings.MinElev + ((settings.MaxElev - settings.MinElev) / 2.0) -44,
                Source0 = scaled
            };

            Result = isUnitTest ? scaled : biased;

        }

        public double GetValue(double x, double z, double y = 0)
        {
            return Result.GetValue(x, y, z);
        }

        public double GetBiomeTemp(double x, double z, double y = 0)
        {
            return GetScaledModuleOutput(temperature.BiomeSelector).GetValue(x, y, z);
        }

        public double GetBiomeHumidity(double x, double z, double y = 0)
        {
            return GetScaledModuleOutput(humidity.BiomeSelector).GetValue(x, y, z);
        }

        public double GetBiomeTerrain(double x, double z, double y = 0)
        {
            return GetScaledModuleOutput(terrain.BiomeSelector).GetValue(x, y, z);
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
                        YScale = 1 / (settings.ContinentSize * 1.01),
                        ZScale = 1 / (settings.ContinentSize * 1.01),
                        Source0 = new ScaleBias
                        {
                            Bias = 0.5 + settings.OceanLandRatio,
                            Scale = 0.5,
                            // Actual noise for continents. Ocean < 0 < Land
                            Source0 = new Perlin
                            {
                                Seed = settings.Seed + 0,
                                Frequency = 0.5 * 0.25,
                                Persistence = 0.5,
                                Lacunarity = 2.508984375,
                                OctaveCount = 4,
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
                    Source0 = plains.Result,
                    Source1 = new Select
                    {
                        Source0 = hills.Result,
                        Source1 = mountains.Result,
                        Control = terrain.BiomeSelector,
                        LowerBound = 0,
                        UpperBound = 0.5,
                        EdgeFalloff = 0.1
                    },
                    Control = terrain.BiomeSelector,
                    LowerBound = 0.3,
                    UpperBound = 0.7,
                    EdgeFalloff = 0.25
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
                    //EdgeFalloff = 0.15,
                    LowerBound = 0.6,
                    UpperBound = 2.0
                }   
            };
        }

        private Module GetScaledModuleOutput(Module input)
        {
            return new ScalePoint
            {
                XScale = 1 / 140.103,
                YScale = 1 / 80.5515,
                ZScale = 1 / 140.103,
                Source0 = input
            };
        }
    }
}
