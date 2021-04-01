using SharpNoise;
using SharpNoise.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.WorldData.Generators.Overworld.Terrain
{
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
        public HillsTerrain(OverworldTerrainSettings ots) : base(ots)
        {
            this.Result = new Cache
            {
                // Sanity check to force results b/w -1.0<y<1.0
                Source0 = new Clamp
                {
                    Source0 = HillsRefined()
                }
            };
        }


        // [Warped-hilly-terrain module]: This turbulence module warps the
        // output value from the coarse-turbulence module.  This turbulence has
        // a higher frequency, but lower power, than the coarse-turbulence
        // module, adding some fine detail to it.
        private Module HillsRefined()
        {
            return new Turbulence
            {
                Seed = settings.Seed + 63,
                Frequency = 6,
                Power = 1.0 / 10107529 * settings.HillsTwist,
                Roughness = 2,
                // [Coarse-turbulence module]: This turbulence module warps the output
                // value from the increased-slope-hilly-terrain module, adding some
                // coarse detail to it.
                Source0 = new Turbulence
                {
                    Seed = settings.Seed + 62,
                    Frequency = 4,
                    Power = 1.0 / 106921 * settings.HillsTwist,
                    Roughness = 2,
                    // [Increased-slope-hilly-terrain module]: To increase the hill slopes at
                    // higher elevations, this exponential-curve module applies an
                    // exponential curve to the output value the scaled-hills-and-valleys
                    // module.  This exponential-curve module expects the input value to
                    // range from -1.0 to 1.0.
                    Source0 = new Exponent
                    {
                        Exp = 1.375,
                        // [Scaled-hills-and-valleys module]: This scale/bias module slightly
                        // reduces the range of the output value from the hills-and-valleys
                        // module, decreasing the heights of the hilltops.
                        Source0 = new ScaleBias
                        {
                            Scale = 0.1, // terrain amplification
                            Bias = 0, // 0 is sea level
                            // [Mountains-and-valleys module]: This blender module merges the
                            // scaled-hills module and the scaled-river-valley module together.  It
                            // causes the low-lying areas of the terrain to become smooth, and causes
                            // the high-lying areas of the terrain to contain hills.  To do this, it
                            // uses the scaled-hills module as the control module, causing the low-
                            // flat module to appear in the lower areas and causing the scaled-river-
                            // valley module to appear in the higher areas.
                            Source0 = HillsShape()
                        }
                    }
                }
            };
        }

        // [Mountains-and-valleys module]: This blender module merges the
        // scaled-hills module and the scaled-river-valley module together.  It
        // causes the low-lying areas of the terrain to become smooth, and causes
        // the high-lying areas of the terrain to contain hills.  To do this, it
        // uses the scaled-hills module as the control module, causing the low-
        // flat module to appear in the lower areas and causing the scaled-river-
        // valley module to appear in the higher areas.
        private Module HillsShape()
        {
            return new Blend
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
                    Scale = -2,
                    Bias = -0.5,
                    // [River-valley module]: This ridged-multifractal-noise module generates
                    // the river valleys.  It has a much lower frequency so that more hills
                    // will appear in between the valleys.  Note that this noise module
                    // generates ridged-multifractal noise using only one octave; this
                    // information will be important in the next step.
                    Source0 = new RidgedMulti
                    {
                        Seed = settings.Seed + 61,
                        Frequency = 3,
                        Lacunarity = settings.HillsLacunarity,
                        OctaveCount = 2,
                        Quality = NoiseQuality.Fast,
                    },
                },
                // [Scaled-hills module]: Next, a scale/bias module scales the output
                // value from the hills module so that its hilltops are not too high.
                // The reason for this is that these hills are eventually added to the
                // river valleys (see below.)
                Control = new ScaleBias
                {
                    Scale = 0.5,
                    Bias = 0.5,
                    // [Hills module]: This billow-noise module generates the hills.
                    Source0 = new Billow
                    {
                        Seed = settings.Seed + 60,
                        Frequency = 8,
                        Persistence = 0.5,
                        Lacunarity = settings.HillsLacunarity,
                        OctaveCount = 3,
                        Quality = NoiseQuality.Fast,
                    },
                },
            };
        }
    }
}
