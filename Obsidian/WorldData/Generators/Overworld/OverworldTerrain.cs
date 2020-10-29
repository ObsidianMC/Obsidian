using Obsidian.Blocks;
using Obsidian.Util.Registry;
using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators.Overworld
{
    internal class OverworldTerrain
    {
        private Perlin cavePerlin;
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

            cavePerlin = new Perlin
            {
                Seed = generatorSettings.Seed,
                Lacunarity = 0,
                Quality = SharpNoise.NoiseQuality.Fast,
                OctaveCount = 2
            };
        }

        public double Terrain(float x, float z)
        {
            //return generatorModule.GetValue(x, 5, z);
            return generatorModule.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch) * generatorSettings.TerrainVertStretch + 30;
        }

        public double Underground(float x, float z)
        {
            return generatorModule.GetValue(x * generatorSettings.TerrainHorizStretch, 1, z * generatorSettings.TerrainHorizStretch) / 45.0;
        }

        public double Bedrock(float x, float z)
        {
            return generatorModule.GetValue(x * generatorSettings.TerrainHorizStretch, 2, z * generatorSettings.TerrainHorizStretch) / 30.0;
        }
    }
}
