using SharpNoise;
using SharpNoise.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.WorldData.Generators.Overworld.Terrain
{
    public class PlainsTerrain : BaseTerrain
    {
        // Generates the plains terrain.
        // Outputs will be between 0 and 1
        public PlainsTerrain(OverworldTerrainSettings ots) : base(ots)
        {
            this.Result = new Cache
            {
                Source0 = new Clamp {
                    UpperBound = 1.0,
                    LowerBound = 0.0,
                    Source0 = new ScalePoint
                    {
                        XScale = 1 / 3.20,
                        YScale = 1 / 16.0,
                        ZScale = 1 / 3.20,
                        Source0 = new Multiply
                        {
                            // [Positive-plains-basis-0 module]: This scale/bias module makes the
                            // output value from the plains-basis-0 module positive since this output
                            // value will be multiplied together with the positive-plains-basis-1
                            // module.
                            Source0 = new ScaleBias
                            {
                                Scale = 0.01, // Flatten -1 < y < 1 to -0.1 < y < 0.1
                                Bias = 0.3, // move -1 < y < 1 up by 0.1
                                // [Plains-basis-0 module]: This billow-noise module, along with the
                                // plains-basis-1 module, produces the plains.
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
            };
        }
    }
}
