using SharpNoise;
using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators.Overworld.Terrain;

public class MountainsTerrain : BaseTerrain
{
    // Generates the hilly terrain.
    //
    // -1.0 represents the lowest elevations and +1.0 represents the highest
    // elevations.
    //
    // [Hilly-terrain group]: Caches the output value from the warped-hilly-
    // terrain module.  This is the output value for the entire hilly-
    // terrain group.
    public MountainsTerrain() : base()
    {
        result = new Cache
        {
            Source0 = new Max
            {
                Source0 = new PlainsTerrain(),
                Source1 = new ScalePoint
                {
                    XScale = 1 / 140.103,
                    YScale = 1 / 140.103,
                    ZScale = 1 / 140.103,
                    Source0 = new ScalePoint
                    {
                        XScale = 1 / 0.9,
                        YScale = 1 / 16.0,
                        ZScale = 1 / 0.9,
                        Source0 = new Clamp
                        {
                            Source0 = new ScaleBias
                            {
                                Scale = 0.22, // Amplification of terrain
                                Bias = 0.15, // lowest level is above sea level (0)
                                Source0 = MountainsBase()
                            }
                        }
                    }
                }
            }
        };
    }

    // This subgroup generates the base-mountain elevations.  Other subgroups
    // will add the ridges and low areas to the base elevations.
    //
    // -1.0 represents low mountainous terrain and +1.0 represents high
    // mountainous terrain.
    //
    // [Mountain-base-definition subgroup]: Caches the output value from the
    // warped-mountains-and-valleys module.
    private Cache MountainsBase()
    {
        return new Cache
        {
            // [Warped-mountains-and-valleys module]: This turbulence module warps
            // the output value from the coarse-turbulence module.  This turbulence
            // has a higher frequency, but lower power, than the coarse-turbulence
            // module, adding some fine detail to it.
            Source0 = new Turbulence
            {
                Seed = settings.Seed + 33,
                Frequency = 12,
                Power = 1.0 / 1020157.0 * settings.MountainsTwist,
                // [Coarse-turbulence module]: This turbulence module warps the output
                // value from the mountain-and-valleys module, adding some coarse detail
                // to it.
                Source0 = new Turbulence
                {
                    Seed = settings.Seed + 32,
                    Frequency = 6,
                    Power = 1.0 / 60730.0 * settings.MountainsTwist,
                    Roughness = 1,
                    // [Mountains-and-valleys module]: This blender module merges the
                    // scaled-mountain-ridge module and the scaled-river-valley module
                    // together.  It causes the low-lying areas of the terrain to become
                    // smooth, and causes the high-lying areas of the terrain to contain
                    // ridges.  To do this, it uses the scaled-river-valley module as the
                    // control module, causing the low-flat module to appear in the lower
                    // areas and causing the scaled-mountain-ridge module to appear in the
                    // higher areas.
                    Source0 = new Blend
                    {
                        // [Low-flat module]: This low constant value is used by
                        // the Mountains-and-valleys module.
                        Source0 = new Constant
                        {
                            ConstantValue = -1,
                        },
                        // [Scaled-river-valley module]: Next, a scale/bias module applies a
                        // scaling factor of -2.0 to the output value from the river-valley
                        // module.  This stretches the possible elevation values because one-
                        // octave ridged-multifractal noise has a lower range of output values
                        // than multiple-octave ridged-multifractal noise.  The negative scaling
                        // factor inverts the range of the output value, turning the ridges from
                        // the river-valley module into valleys.
                        Source1 = new ScaleBias
                        {
                            Scale = 0.5,
                            Bias = 0.375,
                            // [Mountain-ridge module]: This ridged-multifractal-noise module
                            // generates the mountain ridges.
                            Source0 = new RidgedMulti
                            {
                                Seed = settings.Seed + 30,
                                Frequency = 3,
                                Lacunarity = settings.MountainLacunarity,
                                OctaveCount = 4,
                                Quality = NoiseQuality.Fast,
                            },
                        },
                        // [Scaled-mountain-ridge module]: Next, a scale/bias module scales the
                        // output value from the mountain-ridge module so that its ridges are not
                        // too high.  The reason for this is that another subgroup adds actual
                        // mountainous terrain to these ridges.
                        Control = new ScaleBias
                        {
                            Scale = -2,
                            Bias = -0.5,
                            // [River-valley module]: This ridged-multifractal-noise module generates
                            // the river valleys.  It has a much lower frequency than the mountain-
                            // ridge module so that more mountain ridges will appear outside of the
                            // valleys.  Note that this noise module generates ridged-multifractal
                            // noise using only one octave; this information will be important in the
                            // next step.
                            Source0 = new RidgedMulti
                            {
                                Seed = settings.Seed + 31,
                                Frequency = 1.23,
                                Lacunarity = settings.MountainLacunarity,
                                OctaveCount = 1,
                                Quality = NoiseQuality.Fast,
                            },
                        },
                    },
                },
            },
        };
    }
}
