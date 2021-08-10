using Obsidian.API.Noise;
using Obsidian.WorldData.Generators.Overworld.Terrain;
using SharpNoise.Modules;
using System;

namespace Obsidian.WorldData.Generators.Overworld.BiomeNoise
{
    internal class VoronoiBiomeNoise
    {
        private static readonly Lazy<VoronoiBiomeNoise> lazy = new(() => new VoronoiBiomeNoise());

        public static VoronoiBiomeNoise Instance { get { return lazy.Value; } }

        private readonly int seed;

        internal readonly Module result;

        private readonly BaseTerrain ocean, deepocean, badlands, plains, hills, mountains, rivers;

        internal VoronoiBiomeNoise()
        {
            seed = OverworldGenerator.GeneratorSettings.Seed;

            var pass1 = new Turbulence
            {
                Frequency = 0.007519,
                Power = 16,
                Roughness = 3,
                Seed = seed + 123,
                Source0 = new BitShiftInput
                {
                    Amount = 3,
                    Left = false,
                    Source0 = new Cache
                    {
                        Source0 = new VoronoiBiomes
                        {
                            RiverSize = 0.05,
                            Frequency = 0.014159,
                            Seed = seed
                        }
                    }
                }

            };

            result = new Cache {
                // This Select adds Mooshroom Biomes
                Source0 = new Select
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
                            Seed = seed + 11,
                            Power = 11,
                            Roughness = 2,
                            Frequency = 0.0564,
                            Source0 = new Cell
                            {
                                Seed = seed + 9,
                                Frequency = 0.0102345,
                                Type = Cell.CellType.Voronoi
                            },
                        }
                    }
                }
            };
        }
    }
}
