using ICSharpCode.SharpZipLib;
using SharpNoise;
using SharpNoise.Modules;
using System.Collections.Generic;

namespace Obsidian.WorldData.Generators.Overworld
{
    public class OverworldNoise
    {
        private Simplex cavePerlin;
        private Multiply coalNoise;

        private Module BiomeNoise;
        private Module BiomeHumidity;

        private OverworldTerrainSettings generatorSettings;
        private OverworldTerrainGenerator generator;
        private Module generatorModule;

        public OverworldNoise()
        {
            generatorSettings = new OverworldTerrainSettings(); //TODO: load settings from a file
            generator = new OverworldTerrainGenerator(generatorSettings);
            generatorModule = generator.CreateModule();

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

            BiomeNoise = new Curve
            {
                ControlPoints = new List<Curve.ControlPoint>
                {
                    new Curve.ControlPoint(-2.000,  2.000),
                    new Curve.ControlPoint(-1.000,  1.000),
                    new Curve.ControlPoint(-0.125,  0.875),
                    new Curve.ControlPoint( 0.000, -1.000),
                    new Curve.ControlPoint( 1.000, -1.500),
                    new Curve.ControlPoint( 2.000, -2.000),
                },
                // [Large-river-basis module]: This ridged-multifractal-noise module
                // creates the large, deep rivers.
                Source0 = new RidgedMulti
                {
                    Seed = generatorSettings.Seed + 100,
                    Frequency = 18.75,
                    Lacunarity = generatorSettings.ContinentLacunarity,
                    OctaveCount = 1,
                    Quality = NoiseQuality.Best,
                },
            };

            BiomeHumidity = new Curve
            {
                ControlPoints = new List<Curve.ControlPoint>
                {
                    new Curve.ControlPoint(-2.000,  2.0000),
                    new Curve.ControlPoint(-1.000,  1.5000),
                    new Curve.ControlPoint(-0.125,  1.4375),
                    new Curve.ControlPoint( 0.000,  0.5000),
                    new Curve.ControlPoint( 1.000,  0.2500),
                    new Curve.ControlPoint( 2.000,  0.0000),
                },
                // [Small-river-basis module]: This ridged-multifractal-noise module
                // creates the small, shallow rivers.
                Source0 = new RidgedMulti
                {
                    Seed = generatorSettings.Seed + 101,
                    Frequency = 43.25,
                    Lacunarity = generatorSettings.ContinentLacunarity,
                    OctaveCount = 1,
                    Quality = NoiseQuality.Best,
                },
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

        public double GetBiomeTemp(int x, int y, int z)
        {
            return BiomeNoise.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch);
        }

        public double GetBiomeHumidity(int x, int y, int z)
        {
            return BiomeHumidity.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch);
        }

        /// <summary>
        /// Returns values b/w 0 and 5;
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <returns></returns>
        public double Decoration(double x, double y, double z) => BiomeNoise.GetValue(x, y+5, z);
    }
}
