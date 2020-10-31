using ICSharpCode.SharpZipLib;
using SharpNoise.Modules;


namespace Obsidian.WorldData.Generators.Overworld
{
    public class OverworldNoise
    {
        private Simplex cavePerlin;
        private Perlin biomePerlin;
        private Multiply coalNoise;

        private Perlin BiomeTemperatureNoise;

        private OverworldTerrainSettings generatorSettings;
        private OverworldTerrainGenerator generator;
        private Module generatorModule;

        public OverworldNoise()
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
                Frequency = 1.14,
                Lacunarity = 2.0,
                OctaveCount = 2,
                Persistence = 1.53
            };

            coalNoise = new Multiply
            {
                Source0 = new Checkerboard(),
                Source1 = new Perlin
                {
                    Frequency = 1.14,
                    Lacunarity = 2.222,
                    Seed = generatorSettings.Seed
                }
            };

            BiomeTemperatureNoise = new Perlin
            {
                Seed = generatorSettings.Seed,
                Frequency = 3.14,
                Lacunarity = 2.1337,
                OctaveCount = 2,
                Persistence = 2
            };
        }

        public double Terrain(float x, float z)
        {

            //return generatorModule.GetValue(x, 5, z);
            return generatorModule.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch) * generatorSettings.TerrainVertStretch + 27;
        }

        public double Underground(float x, float z)
        {
            return generatorModule.GetValue(x * generatorSettings.TerrainHorizStretch, 1, z * generatorSettings.TerrainHorizStretch) / 45.0;
        }

        public double Bedrock(float x, float z)
        {
            return generatorModule.GetValue(x, 2, z) / 30.0;
        }

        public bool Cave(float x, float y, float z)
        {
            var value = cavePerlin.GetValue(x * generatorSettings.CaveHorizStretch, y * generatorSettings.CaveVertStretch, z * generatorSettings.CaveHorizStretch);
            return value < generatorSettings.CaveFillPercent + 0.1 && value > -0.1;
        }

        public bool Coal(float x, float y, float z)
        {
            var value = coalNoise.GetValue(x / 18, y / 6, z / 18);
            return value < 0.05 && value > 0;
        }

        public bool isRiver(float x, float z)
        {
            return generator.RiversPos.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch) < 0.2;
        }

        public bool isMountain(float x, float z)
        {
            return generator.ContinentsWithMountains.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch) > 0.1;
        }

        public bool isHills(float x, float z)
        {
            var value = generator.ContinentsWithHills.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch);
            return value > 0 && value < 0.5;
        }

        public bool isBadlands(float x, float z)
        {
            var value = generator.ContinentsWithBadlands.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch);
            return value > 0;
        }

        public bool isPlains(float x, float z)
        {
            var value = generator.ContinentsWithPlains.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch);
            return value > -0.1;
        }

        public bool isDeepOcean(float x, float z)
        {
            var value = generator.BaseContinents.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch);
            return value < -0.4;
        }

        public double GetBiome(int x, int y, int z)
        {
            return BiomeTemperatureNoise.GetValue(x * 0.0001, y, z * 0.0001);
        }

        /// <summary>
        /// Returns values b/w 0 and 5;
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <returns></returns>
        public double Decoration(double x, double y, double z) => BiomeTemperatureNoise.GetValue(x, y+5, z);
    }
}
