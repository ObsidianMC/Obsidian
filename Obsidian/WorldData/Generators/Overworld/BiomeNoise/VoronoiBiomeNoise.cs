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

        internal VoronoiBiomeNoise()
        {
            seed = OverworldGenerator.GeneratorSettings.Seed;

            var pass1 = new Turbulence
            {
                Frequency = 0.007519,
                Power = 45,
                Roughness = 5,
                Seed = seed + 123,
                Source0 = new Cache
                {
                    Source0 = new VoronoiBiomes
                    {
                        Frequency = 0.0024159,
                        Seed = seed
                    }
                }
            };

            result = new Cache
            {
                Source0 = pass1
            };
        }
    }
}
