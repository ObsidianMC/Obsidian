using SharpNoise;
using SharpNoise.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.WorldData.Generators.Overworld.Terrain
{
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
            this.Result = new Cache
            {
                // Sanity check to force results b/w -1.0<y<1.0
                Source0 = new ScalePoint
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
                                Scale = 0.25, // Amplification of terrain
                                Bias = 0.33, // lowest level is above sea level (0)
                                Source0 = MountainsBase()
                            }
                        }
                    }
                }
            };
        }

        private Cache BuildMountains()
        {
            var baseMount = MountainsBase();
            return new Cache
            {
                // [Glaciated-mountainous-terrain-module]: This exponential-curve module
                // applies an exponential curve to the output value from the scaled-
                // mountainous-terrain module.  This causes the slope of the mountains to
                // smoothly increase towards higher elevations, as if a glacier grinded
                // out those mountains.  This exponential-curve module expects the output
                // value to range from -1.0 to +1.0.
                Source0 = new Exponent
                {
                    Exp = settings.MountainGlaciation,
                    // [Scaled-mountainous-terrain-module]: This scale/bias module slightly
                    // reduces the range of the output value from the combined-mountainous-
                    // terrain module, decreasing the heights of the mountain peaks.
                    Source0 = new ScaleBias
                    {
                        Scale = 0.8,
                        Bias = 0,
                        // [Combined-mountainous-terrain module]: Note that at this point, the
                        // entire terrain is covered in high mountainous terrain, even at the low
                        // elevations.  To make sure the mountains only appear at the higher
                        // elevations, this selector module causes low mountainous terrain to
                        // appear at the low elevations (within the valleys) and the high
                        // mountainous terrain to appear at the high elevations (within the
                        // ridges.)  To do this, this noise module selects the output value from
                        // the added-high-mountainous-terrain module if the output value from the
                        // mountain-base-definition subgroup is higher than a set amount.
                        // Otherwise, this noise module selects the output value from the scaled-
                        // low-mountainous-terrain module.
                        Source0 = new Select
                        {
                            // [Scaled-low-mountainous-terrain module]: First, this scale/bias module
                            // scales the output value from the low-mountainous-terrain subgroup to a
                            // very low value and biases it towards -1.0.  This results in the low
                            // mountainous areas becoming more-or-less flat with little variation.
                            // This will also result in the low mountainous areas appearing at the
                            // lowest elevations in this subgroup.
                            Source0 = new ScaleBias
                            {
                                Scale = 0.03125,
                                Bias = -0.96875,
                                Source0 = MountainsLow(),
                            },
                            // [Added-high-mountainous-terrain module]: This addition module adds the
                            // output value from the scaled-high-mountainous-terrain module to the
                            // output value from the mountain-base-definition subgroup.  Mountains
                            // now appear all over the terrain.
                            Source1 = new Add
                            {
                                // [Scaled-high-mountainous-terrain module]: Next, this scale/bias module
                                // scales the output value from the high-mountainous-terrain subgroup to
                                // 1/4 of its initial value and biases it so that its output value is
                                // usually positive.
                                Source0 = new ScaleBias
                                {
                                    Scale = 0.25,
                                    Bias = 0.75,
                                    Source0 = MountainsHigh(),
                                },
                                Source1 = baseMount,
                            },
                            Control = baseMount,
                        },
                    },
                },
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


        // Generates the mountainous terrain that appears at high
        // elevations within the mountain ridges.
        //
        // -1.0 represents the lowest elevations and +1.0 represents the highest
        // elevations.
        //
        // [Mountain-base-definition subgroup]: Caches the output value from the
        // warped-mountains-and-valleys module.
        private Cache MountainsHigh()
        {
            return new Cache
            {
                // [Warped-mountains-and-valleys module]: This turbulence module warps
                // the output value from the coarse-turbulence module.  This turbulence
                // has a higher frequency, but lower power, than the coarse-turbulence
                // module, adding some fine detail to it.
                Source0 = new Turbulence
                {
                    Seed = settings.Seed + 42,
                    Frequency = 11,
                    Power = 1.0 / 1080371.0 * settings.MountainsTwist,
                    Roughness = 4,
                    // [High-mountains module]: Next, a maximum-value module causes more
                    // mountains to appear at the expense of valleys.  It does this by
                    // ensuring that only the maximum of the output values from the two
                    // ridged-multifractal-noise modules contribute to the output value of
                    // this subgroup.
                    Source0 = new Max
                    {
                        // [Mountain-basis-0 module]: This ridged-multifractal-noise module,
                        // along with the mountain-basis-1 module, generates the individual
                        // mountains.
                        Source0 = new RidgedMulti
                        {
                            Seed = settings.Seed + 40,
                            Frequency = 4.5,
                            Lacunarity = settings.MountainLacunarity,
                            OctaveCount = 3,
                            Quality = NoiseQuality.Fast,
                        },
                        // [Mountain-basis-1 module]: This ridged-multifractal-noise module,
                        // along with the mountain-basis-0 module, generates the individual
                        // mountains.
                        Source1 = new RidgedMulti
                        {
                            Seed = settings.Seed + 41,
                            Frequency = 4.7,
                            Lacunarity = settings.MountainLacunarity,
                            OctaveCount = 3,
                            Quality = NoiseQuality.Fast,
                        },
                    },
                }
            };
        }


        // Generates the mountainous terrain that appears at low
        // elevations within the river valleys.
        //
        // -1.0 represents the lowest elevations and +1.0 represents the highest
        // elevations.
        //
        // [Low-mountainous-terrain subgroup]: Caches the output value from the
        // low-moutainous-terrain module.
        private Cache MountainsLow()
        {
            return new Cache
            {
                Source0 = new Multiply
                {
                    // [Lowland-basis-0 module]: This ridged-multifractal-noise module,
                    // along with the lowland-basis-1 module, produces the low mountainous
                    // terrain.
                    Source0 = new RidgedMulti
                    {
                        Seed = settings.Seed + 50,
                        Frequency = 14,
                        Lacunarity = settings.MountainLacunarity,
                        OctaveCount = 8,
                        Quality = NoiseQuality.Fast,
                    },
                    // [Lowland-basis-1 module]: This ridged-multifractal-noise module,
                    // along with the lowland-basis-0 module, produces the low mountainous
                    // terrain.
                    Source1 = new RidgedMulti
                    {
                        Seed = settings.Seed + 51,
                        Frequency = 15.5,
                        Lacunarity = settings.MountainLacunarity,
                        OctaveCount = 4,
                        Quality = NoiseQuality.Fast,
                    },
                },
            };
        }
    }
}
