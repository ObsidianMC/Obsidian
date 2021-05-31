using SharpNoise;
using SharpNoise.Modules;
using System.Collections.Generic;

namespace Obsidian.WorldData.Generators.Overworld.BiomeNoise
{
    public class HumidityNoise : BaseBiomeNoise
    {
        public HumidityNoise(OverworldTerrainSettings ots) : base(ots)
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
                        new Curve.ControlPoint(-3.000 - settings.BiomeHumidityRatio,  0.000),
                        new Curve.ControlPoint(-0.350 - settings.BiomeHumidityRatio,  0.000),
                        new Curve.ControlPoint(-0.345 - settings.BiomeHumidityRatio,  1.000),
                        new Curve.ControlPoint(-0.320 - settings.BiomeHumidityRatio,  1.000),
                        new Curve.ControlPoint(-0.315 - settings.BiomeHumidityRatio,  0.000),
                        new Curve.ControlPoint( 0.315 - settings.BiomeHumidityRatio,  0.000),
                        new Curve.ControlPoint( 0.320 - settings.BiomeHumidityRatio,  1.000),
                        new Curve.ControlPoint( 0.345 - settings.BiomeHumidityRatio,  1.000),
                        new Curve.ControlPoint( 0.350 - settings.BiomeHumidityRatio,  0.000),
                        new Curve.ControlPoint( 3.000 - settings.BiomeHumidityRatio,  0.000),
                    },
                    Source0 = Noise()
                }
            };
        }

        private Module Noise()
        {
            return new Cache
            {
                Source0 = new ScalePoint
                {
                    XScale = 1 / (settings.BiomeSize * this.biomeScale),
                    YScale = 1 / (settings.BiomeSize * this.biomeScale),
                    ZScale = 1 / (settings.BiomeSize * this.biomeScale),
                    Source0 = new Perlin
                    {
                        Seed = settings.Seed + 200,
                        Frequency = 0.5 * 0.25,
                        Persistence = 0.5,
                        Lacunarity = 2.508984375,
                        OctaveCount = 4,
                        Quality = NoiseQuality.Best,
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
                        new Curve.ControlPoint(-3.00 - settings.BiomeHumidityRatio, -1.000),
                        new Curve.ControlPoint(-0.33 - settings.BiomeHumidityRatio, -1.000),
                        new Curve.ControlPoint(-0.33 - settings.BiomeHumidityRatio,  0.000),
                        new Curve.ControlPoint( 0.33 - settings.BiomeHumidityRatio,  0.000),
                        new Curve.ControlPoint( 0.33 - settings.BiomeHumidityRatio,  1.000),
                        new Curve.ControlPoint( 3.00 - settings.BiomeHumidityRatio,  1.000),
                    },
                    Source0 = Noise()
                }
            };
        }
    }
}