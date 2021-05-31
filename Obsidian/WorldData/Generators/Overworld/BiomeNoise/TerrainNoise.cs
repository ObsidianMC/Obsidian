using SharpNoise;
using SharpNoise.Modules;
using System.Collections.Generic;

namespace Obsidian.WorldData.Generators.Overworld.BiomeNoise
{
    public class TerrainNoise : BaseBiomeNoise
    {
        public TerrainNoise(OverworldTerrainSettings ots) : base(ots)
        {
            this.BiomeSelector = BiomeSelectorNoise();

            this.RiverSelector = Rivers();
        }


        private Module Rivers()
        {
            return new Cache
            {
                Source0 = new Curve
                {
                    ControlPoints = new List<Curve.ControlPoint>
                    {
                        new Curve.ControlPoint(-3.000 - settings.BiomeTerrainRatio,  0.000),
                        new Curve.ControlPoint(-0.350 - settings.BiomeTerrainRatio,  0.000),
                        new Curve.ControlPoint(-0.345 - settings.BiomeTerrainRatio,  1.000),
                        new Curve.ControlPoint(-0.320 - settings.BiomeTerrainRatio,  1.000),
                        new Curve.ControlPoint(-0.315 - settings.BiomeTerrainRatio,  0.000),
                        new Curve.ControlPoint( 0.315 - settings.BiomeTerrainRatio,  0.000),
                        new Curve.ControlPoint( 0.320 - settings.BiomeTerrainRatio,  1.000),
                        new Curve.ControlPoint( 0.345 - settings.BiomeTerrainRatio,  1.000),
                        new Curve.ControlPoint( 0.350 - settings.BiomeTerrainRatio,  0.000),
                        new Curve.ControlPoint( 3.000 - settings.BiomeTerrainRatio,  0.000),
                    },
                    Source0 = Noise()
                }
            };
        }

        private Module Noise()
        {
            return new Cache
            {
                Source0 = new Clamp {
                    Source0 = new ScalePoint
                    {
                        XScale = 1 / (settings.BiomeSize * this.biomeScale),
                        YScale = 1 / (settings.BiomeSize * this.biomeScale),
                        ZScale = 1 / (settings.BiomeSize * this.biomeScale),
                        Source0 = new Perlin
                        {
                            Seed = settings.Seed + 203,
                            Frequency = 0.5 * 0.25,
                            Persistence = 0.5,
                            Lacunarity = 2.508984375,
                            OctaveCount = 4,
                            Quality = NoiseQuality.Best,
                        }
                    }
                }
            };
        }

        private Module BiomeSelectorNoise()
        {
            return new Cache
            {
                Source0 = new Curve
                {
                    ControlPoints = new List<Curve.ControlPoint>
                    {
                        new Curve.ControlPoint(-1.00 - settings.BiomeTerrainRatio,  0.000),
                        new Curve.ControlPoint(-0.4 - settings.BiomeTerrainRatio,  0.000),
                        new Curve.ControlPoint(-0.33 - settings.BiomeTerrainRatio,  0.500),
                        new Curve.ControlPoint( 0.33 - settings.BiomeTerrainRatio,  0.500),
                        new Curve.ControlPoint( 0.4 - settings.BiomeTerrainRatio,  1.000),
                        new Curve.ControlPoint( 1.00 - settings.BiomeTerrainRatio,  1.000),
                    },
                    Source0 = Noise()
                }
            };
        }
    }
}