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
        //
        // Because this subgroup will eventually be flattened considerably, the
        // types and combinations of noise modules that generate the plains are not
        // really that important; they only need to "look" interesting.
        //
        // -1.0 represents the lowest elevations and +1.0 represents the highest
        // elevations.
        //
        // [Plains-terrain group]: Caches the output value from the rescaled-
        // plains-basis module.  This is the output value for the entire plains-
        // terrain group.
        public PlainsTerrain(OverworldTerrainSettings ots) : base(ots)
        {
            this.Result = new Cache
            {
                // Sanity check to force results b/w -1.0<y<1.0
                Source0 = new Clamp {
                    // [Rescaled-plains-basis module]: This scale/bias module maps the output
                    // value that ranges from 0.0 to 1.0 back to a value that ranges from
                    // -1.0 to +1.0.
                    Source0 = new ScaleBias
                    {
                        Scale = 0.1, // Amplification of terrain
                        Bias = 0, // Bias 0 means sea level
                        Source0 = new Multiply
                        {
                            // [Positive-plains-basis-0 module]: This scale/bias module makes the
                            // output value from the plains-basis-0 module positive since this output
                            // value will be multiplied together with the positive-plains-basis-1
                            // module.
                            Source0 = new ScaleBias
                            {
                                Scale = 0.3,
                                Bias = 0.5,
                                // [Plains-basis-0 module]: This billow-noise module, along with the
                                // plains-basis-1 module, produces the plains.
                                Source0 = new Billow
                                {
                                    Seed = settings.Seed + 70,
                                    Frequency = 8.5,
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
                                    Frequency = 1.5,
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
