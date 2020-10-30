using Obsidian.Blocks;
using Obsidian.Util.Registry;
using SharpNoise.Models;
using SharpNoise.Modules;
using System.Runtime.InteropServices;

namespace Obsidian.WorldData.Generators.Overworld
{
    internal class OverworldTerrain
    {
        private Simplex cavePerlin;
        private Perlin biomePerlin;
        private Multiply oreNoise;

        private OverworldTerrainSettings generatorSettings;
        private OverworldTerrainGenerator generator;
        private Module generatorModule;

        public OverworldTerrain()
        {
            generatorSettings = new OverworldTerrainSettings(); //TODO: load settings from a file
            generator = new OverworldTerrainGenerator(generatorSettings);
            generatorModule = generator.CreateModule();

            biomePerlin = new Perlin
            {
                Seed = generatorSettings.Seed,
                Frequency = 3
            };

            cavePerlin = new Simplex
            {
                Frequency = 3.14,
                Lacunarity = 1.7234,
                OctaveCount = 3,
                Persistence = 0.53
            };

            oreNoise = new Multiply
            {
                Source0 = new Checkerboard(),
                Source1 = new Perlin
                {
                    Frequency = 1.14,
                    Lacunarity = 2.222,
                    Seed = generatorSettings.Seed
                }
            };
        }

        public double Terrain(float x, float z)
        {
            //return generatorModule.GetValue(x, 5, z);
            return generatorModule.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch) * generatorSettings.TerrainVertStretch + 25;
        }

        public double Underground(float x, float z)
        {
            return generatorModule.GetValue(x * generatorSettings.TerrainHorizStretch, 1, z * generatorSettings.TerrainHorizStretch) / 45.0;
        }

        public double Bedrock(float x, float z)
        {
            return generatorModule.GetValue(x * generatorSettings.TerrainHorizStretch, 2, z * generatorSettings.TerrainHorizStretch) / 30.0;
        }

        public bool Cave(float x, float y, float z)
        {
            var value = cavePerlin.GetValue(x * generatorSettings.CaveHorizStretch, y * generatorSettings.CaveVertStretch, z * generatorSettings.CaveHorizStretch);
            return value < generatorSettings.CaveFillPercent && value > 0;
        }

        public bool Coal(float x, float y, float z)
        {
            var value = oreNoise.GetValue(x/18, y/6, z/18);
            return value < 0.05 && value > 0;
        }
    }
}
