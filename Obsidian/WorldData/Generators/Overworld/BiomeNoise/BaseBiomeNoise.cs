using SharpNoise;
using SharpNoise.Modules;
using System.Collections.Generic;
using static SharpNoise.Modules.Curve;

namespace Obsidian.WorldData.Generators.Overworld.BiomeNoise
{
    public class BaseBiomeNoise
    {
        public Module BiomeSelector { get; protected set; }

        public Module RiverSelector { get; protected set; }

        public readonly OverworldTerrainSettings settings;

        protected readonly double biomeScale = 0.812;

        protected int SeedOffset { get; }

        protected double CurveOffset { get; }

        public double Decoration(double x, double y, double z)
        {
            var noise = new Multiply
            {
                Source0 = new Checkerboard(),
                Source1 = new Perlin
                {
                    Frequency = 1.14,
                    Lacunarity = 2.222,
                    Seed = settings.Seed
                }
            };

            return noise.GetValue(x, y, z);
        }

        protected BaseBiomeNoise(OverworldTerrainSettings ots, int seedOffset, double curveOffset)
        {
            settings = ots;
            SeedOffset = seedOffset;
            CurveOffset = curveOffset;

            BiomeSelector = BiomeSelectorNoise();
            RiverSelector = Rivers();
        }

        protected virtual Module Noise()
        {
            return new Cache
            {
                Source0 = new Clamp
                {
                    Source0 = new ScalePoint
                    {
                        XScale = 1 / (settings.BiomeSize * biomeScale),
                        ZScale = 1 / (settings.BiomeSize * biomeScale),
                        Source0 = new Perlin
                        {
                            Seed = settings.Seed + SeedOffset,
                            Frequency = 0.325,
                            Persistence = 0.5,
                            Lacunarity = 1.308984375,
                            OctaveCount = 2,
                            Quality = NoiseQuality.Best,
                        }
                    }
                }
            };
        }

        protected virtual Module Rivers()
        {
            return new Cache
            {
                Source0 = new Curve
                    {
                    Source0 = Noise(),
                    ControlPoints = new List<ControlPoint> {
                        new Curve.ControlPoint(-3.000 - CurveOffset, 0.0),
                        new Curve.ControlPoint(-0.350 - CurveOffset, 0.0),
                        new Curve.ControlPoint(-0.334 - CurveOffset, 1.0),
                        new Curve.ControlPoint(-0.332 - CurveOffset, 1.0),
                        new Curve.ControlPoint(-0.300 - CurveOffset, 0.0),

                        new Curve.ControlPoint(0.330 - CurveOffset, 0.0),
                        new Curve.ControlPoint(0.332 - CurveOffset, 1.0),
                        new Curve.ControlPoint(0.334 - CurveOffset, 1.0),
                        new Curve.ControlPoint(0.350 - CurveOffset, 0.0),
                        new Curve.ControlPoint(3.000 - CurveOffset, 0.0),
                    }
                }
            };
        }

        protected virtual Module BiomeSelectorNoise()
        {
            return Noise();
        }
    }
}
