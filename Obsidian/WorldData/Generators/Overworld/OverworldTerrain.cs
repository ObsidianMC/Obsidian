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
            return cavePerlin.GetValue(x*generatorSettings.CaveHorizStretch, y*generatorSettings.CaveVertStretch, z*generatorSettings.CaveHorizStretch) > generatorSettings.CaveFillPercent;
        }
    }
}
