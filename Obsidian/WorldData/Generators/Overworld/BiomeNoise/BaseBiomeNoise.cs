using SharpNoise.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SharpNoise.Modules.Curve;

namespace Obsidian.WorldData.Generators.Overworld.BiomeNoise
{
    public class BaseBiomeNoise
    {
        public Module BiomeSelector { get; protected set; }

        public Module RiverSelector { get; protected set; }

        protected readonly OverworldTerrainSettings settings;

        protected readonly double biomeScale = 0.812;

        protected List<ControlPoint> RiverControlPoints { get; private set; }

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

        public double Terrain(double x, double z)
        {
            return 0;
        }

        protected BaseBiomeNoise(OverworldTerrainSettings ots)
        {
            this.settings = ots;
            RiverControlPoints = new List<ControlPoint> {
                new Curve.ControlPoint(-3.000 - settings.BiomeHumidityRatio, 0.000),
                new Curve.ControlPoint(-0.45 - settings.BiomeHumidityRatio, 0.000),
                new Curve.ControlPoint(-0.335 - settings.BiomeHumidityRatio, 1.000),
                new Curve.ControlPoint(-0.320 - settings.BiomeHumidityRatio, 1.000),
                new Curve.ControlPoint(-0.30 - settings.BiomeHumidityRatio, 0.000),

                new Curve.ControlPoint(0.30 - settings.BiomeHumidityRatio, 0.000),
                new Curve.ControlPoint(0.320 - settings.BiomeHumidityRatio, 1.000),
                new Curve.ControlPoint(0.335 - settings.BiomeHumidityRatio, 1.000),
                new Curve.ControlPoint(0.45 - settings.BiomeHumidityRatio, 0.000),
                new Curve.ControlPoint(3.000 - settings.BiomeHumidityRatio, 0.000),
            };
        }
    }
}
