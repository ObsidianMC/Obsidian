using SharpNoise;
using SharpNoise.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.WorldData.Generators.Overworld.Terrain
{
    public class OceanTerrain : BaseTerrain
    {
        // Generates the Ocean terrain.
        // Outputs will be between -0.02 and -0.5
        public OceanTerrain(OverworldTerrainSettings ots) : base(ots)
        {
            this.Result = new Cache
            {
                Source0 = new Clamp
                {
                    UpperBound = 0.0,
                    LowerBound = -1.0,
                    Source0 = new ScalePoint
                    {
                        XScale = 1 / 12.20,
                        YScale = 1 / 1.0,
                        ZScale = 1 / 12.20,
                        Source0 = new Turbulence
                        {
                            Frequency = 39.4578,
                            Power = 0.078,
                            Roughness = 3,
                            Seed = ots.Seed + 72,
                            Source0 = new Multiply
                            {
                                Source0 = new ScaleBias
                                {
                                    Scale = 0.005, // Flatten
                                    Bias = -0.4, // move elevation
                                    Source0 = new Billow
                                    {
                                        Seed = settings.Seed + 70,
                                        Frequency = 18.5,
                                        Persistence = 0.5,
                                        Lacunarity = settings.PlainsLacunarity,
                                        OctaveCount = 3,
                                        Quality = NoiseQuality.Standard,
                                    }
                                },
                                Source1 = new ScaleBias
                                {
                                    Scale = 0.2,
                                    Bias = 0.5,
                                    Source0 = new Billow
                                    {
                                        Seed = settings.Seed + 71,
                                        Frequency = 3.5,
                                        Persistence = 0.5,
                                        Lacunarity = settings.PlainsLacunarity,
                                        OctaveCount = 8,
                                        Quality = NoiseQuality.Fast,
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }
    }
}
