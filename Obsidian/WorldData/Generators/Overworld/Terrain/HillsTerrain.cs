using SharpNoise;
using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators.Overworld.Terrain;

public class HillsTerrain : BaseTerrain
{
    // Generates the hilly terrain.
    //
    // -1.0 represents the lowest elevations and +1.0 represents the highest
    // elevations.
    //
    // [Hilly-terrain group]: Caches the output value from the warped-hilly-
    // terrain module.  This is the output value for the entire hilly-
    // terrain group.
    public HillsTerrain() : base()
    {
        this.Result = new Cache
        {
            Source0 = new ScalePoint
            {
                XScale = 1 / 140.103,
                YScale = 1 / 140.103,
                ZScale = 1 / 140.103,
                Source0 = new Clamp
                {
                    UpperBound = 1.0,
                    LowerBound = 0.0,
                    Source0 = new ScalePoint
                    {
                        XScale = 1 / 4.20,
                        YScale = 1 / 16.0,
                        ZScale = 1 / 4.20,
                        Source0 = new Multiply
                        {
                            // [Positive-plains-basis-0 module]: This scale/bias module makes the
                            // output value from the plains-basis-0 module positive since this output
                            // value will be multiplied together with the positive-plains-basis-1
                            // module.
                            Source0 = new ScaleBias
                            {
                                Scale = 0.13, // Flatten -1 < y < 1 to -0.1 < y < 0.1
                                Bias = 0.6, // move -1 < y < 1 up by 0.1
                                            // [Plains-basis-0 module]: This billow-noise module, along with the
                                            // plains-basis-1 module, produces the plains.
                                Source0 = new Billow
                                {
                                    Seed = settings.Seed + 79,
                                    Frequency = 23.5,
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
                                Scale = 0.2,
                                Bias = 0.3,
                                // [Plains-basis-1 module]: This billow-noise module, along with the
                                // plains-basis-2 module, produces the plains.
                                Source0 = new Billow
                                {
                                    Seed = settings.Seed + 78,
                                    Frequency = 13.5,
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
