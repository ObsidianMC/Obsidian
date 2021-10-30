using Obsidian.API.Noise;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;
using Obsidian.WorldData.Generators.Overworld.Carvers;
using SharpNoise.Modules;
using System.Collections.Generic;
using static Obsidian.API.Noise.VoronoiBiomes;
using Blend = Obsidian.API.Noise.Blend;

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

            Dictionary<int, Module> biomesMap = new Dictionary<int, Module>()
            {
                { 0, ocean.Result },
                { 1, plains.Result },
                { 2, plains.Result },
                { 3, mountains.Result },
                { 4, plains.Result },
                { 5, plains.Result },
                { 6, plains.Result },
                { 7, rivers.Result },
                { 10, ocean.Result },
                { 11, rivers.Result },
                { 12, hills.Result },
                { 13, mountains.Result },
                { 14, plains.Result },
                { 15, new Constant { ConstantValue = 0.0 } },
                { 16, new Constant { ConstantValue = 0.0 } },
                { 17, hills.Result },
                { 18, hills.Result },
                { 19, hills.Result },
                { 20, hills.Result },
                { 21, plains.Result },
                { 22, hills.Result },
                { 23, new Constant { ConstantValue = 0.0 } },
                { 24, deepocean.Result },
                { 25, new Constant { ConstantValue = 0.0 } },
                { 26, new Constant { ConstantValue = 0.0 } },
                { 27, plains.Result },
                { 28, hills.Result },
                { 29, plains.Result },
                { 30, plains.Result },
                { 31, hills.Result },
                { 32, plains.Result },
                { 33, hills.Result },
                { 34, mountains.Result },
                { 35, plains.Result },
                { 36, hills.Result },
                { 37, plains.Result },
                { 38, hills.Result },
                { 39, hills.Result },
                { 44, ocean.Result },
                { 45, ocean.Result },
                { 46, ocean.Result },
                { 47, deepocean.Result },
                { 48, deepocean.Result },
                { 49, deepocean.Result },
                { 50, deepocean.Result },
                { 129, plains.Result },
                { 130, plains.Result },
                { 131, mountains.Result },
                { 132, plains.Result },
                { 133, mountains.Result },
                { 134, hills.Result },
                { 140, hills.Result },
                { 149, hills.Result },
                { 151, plains.Result },
                { 155, plains.Result },
                { 156, hills.Result },
                { 157, hills.Result },
                { 158, mountains.Result },
                { 160, plains.Result },
                { 161, hills.Result },
                { 162, mountains.Result },
                { 163, plains.Result },
                { 164, hills.Result },
                { 165, hills.Result },
                { 166, mountains.Result },
                { 167, mountains.Result },
                { 168, plains.Result },
                { 169, hills.Result },
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
                    TerrainModules = biomesMap
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
}
