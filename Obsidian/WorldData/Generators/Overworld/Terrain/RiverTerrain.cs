using SharpNoise;
using SharpNoise.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.WorldData.Generators.Overworld.Terrain
{
    public class RiverTerrain : BaseTerrain
    {
        // Generates the plains terrain.
        // Outputs will be between -0.3 and -0.1
        public RiverTerrain() : base()
        {
            this.Result = new Cache
            {
                Source0 = new ScalePoint
                {
                    XScale = 1 / 14.103,
                    YScale = 1 / 140.103,
                    ZScale = 1 / 14.103,
                    Source0 = new Clamp
                    {
                        UpperBound = 0.0,
                        LowerBound = -0.4,
                        Source0 = new ScalePoint
                        {
                            XScale = 1 / 16.0,
                            YScale = 1 / 16.0,
                            ZScale = 1 / 16.0,
                            Source0 = new Multiply
                            {
                                // [Positive-plains-basis-0 module]: This scale/bias module makes the
                                // output value from the plains-basis-0 module positive since this output
                                // value will be multiplied together with the positive-plains-basis-1
                                // module.
                                Source0 = new ScaleBias
                                {
                                    Scale = 0.06,
                                    Bias = -0.2,
                                    // [Plains-basis-0 module]: This billow-noise module, along with the
                                    // plains-basis-1 module, produces the plains.
                                    Source0 = new Billow
                                    {
                                        Seed = settings.Seed + 70,
                                        Frequency = 18.5,
                                        Persistence = 0.5,
                                        Lacunarity = settings.PlainsLacunarity,
                                        OctaveCount = 1,
                                        Quality = NoiseQuality.Standard,
                                    }
                                },
                                // [Positive-plains-basis-1 module]: This scale/bias module makes the
                                // output value from the plains-basis-1 module positive since this output
                                // value will be multiplied together with the positive-plains-basis-0
                                // module.
                                Source1 = new ScaleBias
                                {
                                    Scale = 0.5,
                                    Bias = 0.5,
                                    // [Plains-basis-1 module]: This billow-noise module, along with the
                                    // plains-basis-2 module, produces the plains.
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
