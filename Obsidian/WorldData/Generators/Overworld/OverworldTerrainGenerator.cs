using SharpNoise;
using SharpNoise.Modules;
using System.Collections.Generic;

namespace Obsidian.WorldData.Generators.Overworld
{
    /// <summary>
    /// Contains planet generator module creation code
    /// </summary>
    /// <remarks>
    /// A port of the complex planetary surface example from libnoise
    /// See http://libnoise.sourceforge.net/examples/complexplanet/index.html
    /// </remarks>
    class OverworldTerrainGenerator
    {
        public OverworldTerrainSettings Settings { get; set; }

        public OverworldTerrainGenerator(OverworldTerrainSettings settings)
        {
            Settings = settings;
        }

        public Module RiverPositions { get; set; }

        #region Module Groups

        Module CreateContinentDefinition()
        {
            // Roughly defines the positions and base elevations of the planet's continents.
            //
            // The "base elevation" is the elevation of the terrain before any terrain
            // features (mountains, hills, etc.) are placed on that terrain.
            //
            // -1.0 represents the lowest elevations and +1.0 represents the highest
            // elevations.
            //
            // [Base-continent-definition subgroup]: Caches the output value from the
            // clamped-continent module.
            var baseContinent = new Cache
            {
                // [Clamped-continent module]: Finally, a clamp module modifies the
                // carved-continent module to ensure that the output value of this
                // subgroup is between -1.0 and 1.0.
                Source0 = new Clamp
                {
                    LowerBound = -1,
                    UpperBound = 1,
                    // [Carved-continent module]: This minimum-value module carves out chunks
                    // from the continent-with-ranges module.  It does this by ensuring that
                    // only the minimum of the output values from the scaled-carver module
                    // and the continent-with-ranges module contributes to the output value
                    // of this subgroup.  Most of the time, the minimum-value module will
                    // select the output value from the continents-with-ranges module since
                    // the output value from the scaled-carver module is usually near 1.0.
                    // Occasionally, the output value from the scaled-carver module will be
                    // less than the output value from the continent-with-ranges module, so
                    // in this case, the output value from the scaled-carver module is
                    // selected.
                    Source0 = new Min
                    {
                        // [Scaled-carver module]: This scale/bias module scales the output
                        // value from the carver module such that it is usually near 1.0.  This
                        // is required for step 5.
                        Source0 = new ScaleBias
                        {
                            Scale = 0.375,
                            Bias = 0.625,
                            // [Carver module]: This higher-frequency Perlin-noise module will be
                            // used by subsequent noise modules to carve out chunks from the mountain
                            // ranges within the continent-with-ranges module so that the mountain
                            // ranges will not be complely impassible.
                            Source0 = new Perlin
                            {
                                Seed = Settings.Seed + 1,
                                Frequency = Settings.ContinentFrequency * 4.34375,
                                Persistence = 0.5,
                                Lacunarity = Settings.ContinentLacunarity,
                                OctaveCount = 11,
                                Quality = NoiseQuality.Standard,
                            },
                        },
                        // [Continent-with-ranges module]: Next, a curve module modifies the
                        // output value from the continent module so that very high values appear
                        // near sea level.  This defines the positions of the mountain ranges.
                        Source1 = new Curve
                        {
                            ControlPoints = new List<Curve.ControlPoint>
                            {
                                new Curve.ControlPoint(-2.0000 + Settings.SeaLevel, -1.625 + Settings.SeaLevel),
                                new Curve.ControlPoint(-1.0000 + Settings.SeaLevel, -1.375 + Settings.SeaLevel),
                                new Curve.ControlPoint( 0.0000 + Settings.SeaLevel, -0.375 + Settings.SeaLevel),
                                new Curve.ControlPoint( 0.0625 + Settings.SeaLevel,  0.125 + Settings.SeaLevel),
                                new Curve.ControlPoint( 0.1250 + Settings.SeaLevel,  0.250 + Settings.SeaLevel),
                                new Curve.ControlPoint( 0.2500 + Settings.SeaLevel,  1.000 + Settings.SeaLevel),
                                new Curve.ControlPoint( 0.5000 + Settings.SeaLevel,  0.250 + Settings.SeaLevel),
                                new Curve.ControlPoint( 0.7500 + Settings.SeaLevel,  0.250 + Settings.SeaLevel),
                                new Curve.ControlPoint( 1.0000 + Settings.SeaLevel,  0.500 + Settings.SeaLevel),
                                new Curve.ControlPoint( 2.0000 + Settings.SeaLevel,  0.500 + Settings.SeaLevel),
                            },
                            // [Continent module]: This Perlin-noise module generates the continents.
                            // This noise module has a high number of octaves so that detail is
                            // visible at high zoom levels.
                            Source0 = new Perlin
                            {
                                Seed = Settings.Seed + 0,
                                Frequency = Settings.ContinentFrequency,
                                Persistence = 0.5,
                                Lacunarity = Settings.ContinentLacunarity,
                                OctaveCount = 14,
                                Quality = NoiseQuality.Standard,
                            },
                        },
                    },
                },
            };

            // Warps the output value from the the base-continent-
            // definition module, producing more realistic terrain.
            //
            // Warping the base continent definition produces lumpier terrain with
            // cliffs and rifts.
            //
            // -1.0 represents the lowest elevations and +1.0 represents the highest
            // elevations.
            //
            // [Continent-definition group]: Caches the output value from the
            // clamped-continent module.  This is the output value for the entire
            // continent-definition group.
            var continentDefinition = new Cache
            {
                // [Select-turbulence module]: At this stage, the turbulence is applied
                // to the entire base-continent-definition subgroup, producing some very
                // rugged, unrealistic coastlines.  This selector module selects the
                // output values from the (unwarped) base-continent-definition subgroup
                // and the warped-base-continent-definition module, based on the output
                // value from the (unwarped) base-continent-definition subgroup.  The
                // selection boundary is near sea level and has a relatively smooth
                // transition.  In effect, only the higher areas of the base-continent-
                // definition subgroup become warped; the underwater and coastal areas
                // remain unaffected.
                Source0 = new Select
                {
                    EdgeFalloff = 0.0625,
                    LowerBound = Settings.SeaLevel - 0.0375,
                    UpperBound = Settings.SeaLevel + 1000.0375,
                    Control = baseContinent,
                    Source0 = baseContinent,
                    // [Warped-base-continent-definition module]: This turbulence module
                    // warps the output value from the intermediate-turbulence module.  This
                    // turbulence has a higher frequency, but lower power, than the
                    // intermediate-turbulence module, adding some fine detail to it.
                    Source1 = new Turbulence
                    {
                        Seed = Settings.Seed + 12,
                        Frequency = Settings.ContinentFrequency * 95.25,
                        Power = Settings.ContinentFrequency / 1019.75,
                        Roughness = 11,
                        // [Intermediate-turbulence module]: This turbulence module warps the
                        // output value from the coarse-turbulence module.  This turbulence has
                        // a higher frequency, but lower power, than the coarse-turbulence
                        // module, adding some intermediate detail to it.
                        Source0 = new Turbulence
                        {
                            Seed = Settings.Seed + 11,
                            Frequency = Settings.ContinentFrequency * 47.25,
                            Power = Settings.ContinentFrequency / 433.75,
                            Roughness = 12,
                            // [Coarse-turbulence module]: This turbulence module warps the output
                            // value from the base-continent-definition subgroup, adding some coarse
                            // detail to it.
                            Source0 = new Turbulence
                            {
                                Seed = Settings.Seed + 10,
                                Frequency = Settings.ContinentFrequency * 15.25,
                                Power = Settings.ContinentFrequency / 113.75,
                                Roughness = 12,
                                Source0 = baseContinent,
                            },
                        },
                    },
                },
            };

            return continentDefinition;
        }


        Module CreateTerrainTypeDefinition(Module continentDefinition)
        {
            // Defines the positions of the terrain types on the planet.
            //
            // Terrain types include, in order of increasing roughness, plains, hills,
            // and mountains.
            //
            // This subgroup's output value is based on the output value from the
            // continent-definition group.  Rougher terrain mainly appears at higher
            // elevations.
            //
            // -1.0 represents the smoothest terrain types (plains and underwater) and
            // +1.0 represents the roughest terrain types (mountains).
            //
            // [Terrain-type-definition group]: Caches the output value from the
            // roughness-probability-shift module.  This is the output value for
            // the entire terrain-type-definition group.
            var terrainTypeDefinition = new Cache
            {
                // [Roughness-probability-shift module]: This terracing module sharpens
                // the edges of the warped-continent module near sea level and lowers
                // the slope towards the higher-elevation areas.  This shrinks the areas
                // in which the rough terrain appears, increasing the "rarity" of rough
                // terrain.
                Source0 = new Terrace
                {
                    ControlPoints = new List<double>
                    {
                        -1,
                        Settings.SeaLevel / 2.0,
                        1,
                    },
                    // [Warped-continent module]: This turbulence module slightly warps the
                    // output value from the continent-definition group.  This prevents the
                    // rougher terrain from appearing exclusively at higher elevations.
                    // Rough areas may now appear in the the ocean, creating rocky islands
                    // and fjords.
                    Source0 = new Turbulence
                    {
                        Seed = Settings.Seed + 20,
                        Frequency = Settings.ContinentFrequency * 18.125,
                        Power = Settings.ContinentFrequency / 20.59375 * Settings.TerrainOffset,
                        Roughness = 3,
                        Source0 = continentDefinition,
                    },
                },
            };

            return terrainTypeDefinition;
        }

        Module CreateMountainousTerrain()
        {
            // This subgroup generates the base-mountain elevations.  Other subgroups
            // will add the ridges and low areas to the base elevations.
            //
            // -1.0 represents low mountainous terrain and +1.0 represents high
            // mountainous terrain.
            //
            // [Mountain-base-definition subgroup]: Caches the output value from the
            // warped-mountains-and-valleys module.
            var mountainBaseDefinition = new Cache
            {
                // [Warped-mountains-and-valleys module]: This turbulence module warps
                // the output value from the coarse-turbulence module.  This turbulence
                // has a higher frequency, but lower power, than the coarse-turbulence
                // module, adding some fine detail to it.
                Source0 = new Turbulence
                {
                    Seed = Settings.Seed + 33,
                    Frequency = 21221,
                    Power = 1.0 / 120157.0 * Settings.MountainsTwist,
                    // [Coarse-turbulence module]: This turbulence module warps the output
                    // value from the mountain-and-valleys module, adding some coarse detail
                    // to it.
                    Source0 = new Turbulence
                    {
                        Seed = Settings.Seed + 32,
                        Frequency = 1337,
                        Power = 1.0 / 6730.0 * Settings.MountainsTwist,
                        Roughness = 4,
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
                                    Seed = Settings.Seed + 30,
                                    Frequency = 1723,
                                    Lacunarity = Settings.MountainLacunarity,
                                    OctaveCount = 4,
                                    Quality = NoiseQuality.Standard,
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
                                    Seed = Settings.Seed + 31,
                                    Frequency = 367,
                                    Lacunarity = Settings.MountainLacunarity,
                                    OctaveCount = 1,
                                    Quality = NoiseQuality.Best,
                                },
                            },
                        },
                    },
                },
            };

            // Generates the mountainous terrain that appears at high
            // elevations within the mountain ridges.
            //
            // -1.0 represents the lowest elevations and +1.0 represents the highest
            // elevations.
            //
            // [Mountain-base-definition subgroup]: Caches the output value from the
            // warped-mountains-and-valleys module.
            var mountainousHigh = new Cache
            {
                // [Warped-mountains-and-valleys module]: This turbulence module warps
                // the output value from the coarse-turbulence module.  This turbulence
                // has a higher frequency, but lower power, than the coarse-turbulence
                // module, adding some fine detail to it.
                Source0 = new Turbulence
                {
                    Seed = Settings.Seed + 42,
                    Frequency = 31511,
                    Power = 1.0 / 180371.0 * Settings.MountainsTwist,
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
                            Seed = Settings.Seed + 40,
                            Frequency = 2371,
                            Lacunarity = Settings.MountainLacunarity,
                            OctaveCount = 3,
                            Quality = NoiseQuality.Best,
                        },
                        // [Mountain-basis-1 module]: This ridged-multifractal-noise module,
                        // along with the mountain-basis-0 module, generates the individual
                        // mountains.
                        Source1 = new RidgedMulti
                        {
                            Seed = Settings.Seed + 41,
                            Frequency = 2341,
                            Lacunarity = Settings.MountainLacunarity,
                            OctaveCount = 3,
                            Quality = NoiseQuality.Best,
                        },
                    },
                }
            };

            // Generates the mountainous terrain that appears at low
            // elevations within the river valleys.
            //
            // -1.0 represents the lowest elevations and +1.0 represents the highest
            // elevations.
            //
            // [Low-mountainous-terrain subgroup]: Caches the output value from the
            // low-moutainous-terrain module.
            var mountainousLow = new Cache
            {
                Source0 = new Multiply
                {
                    // [Lowland-basis-0 module]: This ridged-multifractal-noise module,
                    // along with the lowland-basis-1 module, produces the low mountainous
                    // terrain.
                    Source0 = new RidgedMulti
                    {
                        Seed = Settings.Seed + 50,
                        Frequency = 1381,
                        Lacunarity = Settings.MountainLacunarity,
                        OctaveCount = 8,
                        Quality = NoiseQuality.Best,
                    },
                    // [Lowland-basis-1 module]: This ridged-multifractal-noise module,
                    // along with the lowland-basis-0 module, produces the low mountainous
                    // terrain.
                    Source1 = new RidgedMulti
                    {
                        Seed = Settings.Seed + 51,
                        Frequency = 1427,
                        Lacunarity = Settings.MountainLacunarity,
                        OctaveCount = 8,
                        Quality = NoiseQuality.Best,
                    },
                },
            };

            // Generates the final mountainous terrain by combining the
            // high-mountainous-terrain subgroup with the low-mountainous-terrain
            // subgroup.
            //
            // -1.0 represents the lowest elevations and +1.0 represents the highest
            // elevations.
            //
            // [Mountainous-terrain group]: Caches the output value from the
            // glaciated-mountainous-terrain module.  This is the output value for
            // the entire mountainous-terrain group.
            var mountainousTerrain = new Cache
            {
                // [Glaciated-mountainous-terrain-module]: This exponential-curve module
                // applies an exponential curve to the output value from the scaled-
                // mountainous-terrain module.  This causes the slope of the mountains to
                // smoothly increase towards higher elevations, as if a glacier grinded
                // out those mountains.  This exponential-curve module expects the output
                // value to range from -1.0 to +1.0.
                Source0 = new Exponent
                {
                    Exp = Settings.MountainGlaciation,
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
                                Source0 = mountainousLow,
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
                                    Bias = 0.25,
                                    Source0 = mountainousHigh,
                                },
                                Source1 = mountainBaseDefinition,
                            },
                            Control = mountainBaseDefinition,
                        },
                    },
                },
            };

            return mountainousTerrain;
        }

        Module CreateHillyTerrain()
        {
            // Generates the hilly terrain.
            //
            // -1.0 represents the lowest elevations and +1.0 represents the highest
            // elevations.
            //
            // [Hilly-terrain group]: Caches the output value from the warped-hilly-
            // terrain module.  This is the output value for the entire hilly-
            // terrain group.
            var hillyTerrain = new Cache
            {
                // [Warped-hilly-terrain module]: This turbulence module warps the
                // output value from the coarse-turbulence module.  This turbulence has
                // a higher frequency, but lower power, than the coarse-turbulence
                // module, adding some fine detail to it.
                Source0 = new Turbulence
                {
                    Seed = Settings.Seed + 63,
                    Frequency = 512,
                    Power = 1.0 / 117529 * Settings.HillsTwist,
                    Roughness = 6,
                    // [Coarse-turbulence module]: This turbulence module warps the output
                    // value from the increased-slope-hilly-terrain module, adding some
                    // coarse detail to it.
                    Source0 = new Turbulence
                    {
                        Seed = Settings.Seed + 62,
                        Frequency = 356,
                        Power = 1.0 / 16921 * Settings.HillsTwist,
                        Roughness = 4,
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
                                Scale = 0.75,
                                Bias = -0.25,
                                // [Mountains-and-valleys module]: This blender module merges the
                                // scaled-hills module and the scaled-river-valley module together.  It
                                // causes the low-lying areas of the terrain to become smooth, and causes
                                // the high-lying areas of the terrain to contain hills.  To do this, it
                                // uses the scaled-hills module as the control module, causing the low-
                                // flat module to appear in the lower areas and causing the scaled-river-
                                // valley module to appear in the higher areas.
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
                                        Scale = -2,
                                        Bias = -0.5,
                                        // [River-valley module]: This ridged-multifractal-noise module generates
                                        // the river valleys.  It has a much lower frequency so that more hills
                                        // will appear in between the valleys.  Note that this noise module
                                        // generates ridged-multifractal noise using only one octave; this
                                        // information will be important in the next step.
                                        Source0 = new RidgedMulti
                                        {
                                            Seed = Settings.Seed + 61,
                                            Frequency = 180.5,
                                            Lacunarity = Settings.HillsLacunarity,
                                            OctaveCount = 1,
                                            Quality = NoiseQuality.Best,
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
                                            Seed = Settings.Seed + 60,
                                            Frequency = 1663,
                                            Persistence = 0.5,
                                            Lacunarity = Settings.HillsLacunarity,
                                            OctaveCount = 6,
                                            Quality = NoiseQuality.Best,
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };

            return hillyTerrain;
        }

        Module CreatePlainsTerrain()
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
            var plainsTerrain = new Cache
            {
                // [Rescaled-plains-basis module]: This scale/bias module maps the output
                // value that ranges from 0.0 to 1.0 back to a value that ranges from
                // -1.0 to +1.0.
                Source0 = new ScaleBias
                {
                    Scale = 2,
                    Bias = -1,
                    Source0 = new Multiply
                    {
                        // [Positive-plains-basis-0 module]: This scale/bias module makes the
                        // output value from the plains-basis-0 module positive since this output
                        // value will be multiplied together with the positive-plains-basis-1
                        // module.
                        Source0 = new ScaleBias
                        {
                            Scale = 0.5,
                            Bias = 0.5,
                            // [Plains-basis-0 module]: This billow-noise module, along with the
                            // plains-basis-1 module, produces the plains.
                            Source0 = new Billow
                            {
                                Seed = Settings.Seed + 70,
                                Frequency = 420,
                                Persistence = 0.5,
                                Lacunarity = Settings.PlainsLacunarity,
                                OctaveCount = 2,
                                Quality = NoiseQuality.Best,
                            },
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
                                Seed = Settings.Seed + 71,
                                Frequency = 512.2,
                                Persistence = 0.5,
                                Lacunarity = Settings.PlainsLacunarity,
                                OctaveCount = 3,
                                Quality = NoiseQuality.Best,
                            },
                        },
                    },
                },
            };

            return plainsTerrain;
        }

        Module CreateBadlandsTerrain()
        {
            // Generates the sandy terrain for the badlands.
            //
            // -1.0 represents the lowest elevations and +1.0 represents the highest
            // elevations.
            //
            // [Badlands-sand subgroup]: Caches the output value from the dunes-with-
            // detail module.
            var badlandsSand = new Cache
            {
                // [Dunes-with-detail module]: This addition module combines the scaled-
                // sand-dunes module with the scaled-dune-detail module.
                Source0 = new Add
                {
                    // [Scaled-sand-dunes module]: This scale/bias module shrinks the dune
                    // heights by a small amount.  This is necessary so that the subsequent
                    // noise modules in this subgroup can add some detail to the dunes.
                    Source0 = new ScaleBias
                    {
                        Scale = 0.875,
                        Bias = 0,
                        // [Sand-dunes module]: This ridged-multifractal-noise module generates
                        // sand dunes.  This ridged-multifractal noise is generated with a single
                        // octave, which makes very smooth dunes.
                        Source0 = new RidgedMulti
                        {
                            Seed = Settings.Seed + 80,
                            Frequency = 6163.5,
                            Lacunarity = Settings.BadlandsLacunarity,
                            Quality = NoiseQuality.Best,
                            OctaveCount = 1,
                        },
                    },
                    // [Scaled-dune-detail module]: This scale/bias module shrinks the dune
                    // details by a large amount.  This is necessary so that the subsequent
                    // noise modules in this subgroup can add this detail to the sand-dunes
                    // module.\
                },
            };

            // Generates the cliffs for the badlands.
            //
            // -1.0 represents the lowest elevations and +1.0 represents the highest
            // elevations.
            //
            // [Badlands-cliffs subgroup]: Caches the output value from the warped-
            // cliffs module.
            var badlandsCliffs = new Cache
            {
                // [Warped-cliffs module]: This turbulence module warps the output value
                // from the coarse-turbulence module.  This turbulence has a higher
                // frequency, but lower power, than the coarse-turbulence module, adding
                // some fine detail to it.
                Source0 = new Turbulence
                {
                    Seed = Settings.Seed + 92,
                    Frequency = 36107,
                    Power = 1.0 / 211543.0 * Settings.BadlandsTwist,
                    Roughness = 3,
                    // [Coarse-turbulence module]: This turbulence module warps the output
                    // value from the terraced-cliffs module, adding some coarse detail to
                    // it.
                    Source0 = new Turbulence
                    {
                        Seed = Settings.Seed + 91,
                        Frequency = 16111,
                        Power = 1.0 / 141539.0 * Settings.BadlandsTwist,
                        Roughness = 3,
                        // [Terraced-cliffs module]: Next, this terracing module applies some
                        // terraces to the clamped-cliffs module in the lower elevations before
                        // the sharp cliff transition.
                        Source0 = new Terrace
                        {
                            ControlPoints = new List<double>
                            {
                                -1.0000,
                                -0.8750,
                                -0.7500,
                                -0.5000,
                                 0.0000,
                                 1.0000,
                            },
                            // [Clamped-cliffs module]: This clamping module makes the tops of the
                            // cliffs very flat by clamping the output value from the cliff-shaping
                            // module so that the tops of the cliffs are very flat.
                            Source0 = new Clamp
                            {
                                LowerBound = -999.125,
                                UpperBound = 0.875,
                                // [Cliff-shaping module]: Next, this curve module applies a curve to the
                                // output value from the cliff-basis module.  This curve is initially
                                // very shallow, but then its slope increases sharply.  At the highest
                                // elevations, the curve becomes very flat again.  This produces the
                                // stereotypical Utah-style desert cliffs.
                                Source0 = new Curve
                                {
                                    ControlPoints = new List<Curve.ControlPoint>
                                    {
                                        new Curve.ControlPoint(-2.0000, -2.0000),
                                        new Curve.ControlPoint(-1.0000, -1.2500),
                                        new Curve.ControlPoint(-0.0000, -0.7500),
                                        new Curve.ControlPoint( 0.5000, -0.2500),
                                        new Curve.ControlPoint( 0.6250,  0.8750),
                                        new Curve.ControlPoint( 0.7500,  1.0000),
                                        new Curve.ControlPoint( 2.0000,  1.2500),
                                    },
                                    // [Cliff-basis module]: This Perlin-noise module generates some coherent
                                    // noise that will be used to generate the cliffs.
                                    Source0 = new Perlin
                                    {
                                        Seed = Settings.Seed + 90,
                                        Frequency = Settings.ContinentFrequency * 839,
                                        Persistence = 0.5,
                                        Lacunarity = Settings.BadlandsLacunarity,
                                        OctaveCount = 6,
                                        Quality = NoiseQuality.Standard,
                                    },
                                },
                            },
                        },
                    },
                },
            };

            //Generates the final badlands terrain.
            //
            // Using a scale/bias module, the badlands sand is flattened considerably,
            // then the sand elevations are lowered to around -1.0.  The maximum value
            // from the flattened sand module and the cliff module contributes to the
            // final elevation.  This causes sand to appear at the low elevations since
            // the sand is slightly higher than the cliff base.
            //
            // -1.0 represents the lowest elevations and +1.0 represents the highest
            // elevations.
            //
            // [Badlands-terrain group]: Caches the output value from the dunes-and-
            // cliffs module.  This is the output value for the entire badlands-
            // terrain group.
            var badlandsTerrain = new Cache
            {
                // [Dunes-and-cliffs module]: This maximum-value module causes the dunes
                // to appear in the low areas and the cliffs to appear in the high areas.
                // It does this by selecting the maximum of the output values from the
                // scaled-sand-dunes module and the badlands-cliffs subgroup.
                Source0 = new Max
                {
                    Source0 = badlandsCliffs,
                    // [Scaled-sand-dunes module]: This scale/bias module considerably
                    // flattens the output value from the badlands-sands subgroup and lowers
                    // this value to near -1.0.
                    Source1 = new ScaleBias
                    {
                        Scale = 0.25,
                        Bias = -0.75,
                        Source0 = badlandsSand,
                    },
                },
            };

            return badlandsTerrain;
        }

        Module CreateRiverPositions()
        {
            // Generates the river positions.
            //
            // -1.0 represents the lowest elevations and +1.0 represents the highest
            // elevations.
            //
            // [River-positions group]: Caches the output value from the warped-
            // rivers module.  This is the output value for the entire river-
            // positions group.
            var riverPositions = new Cache
            {
                // [Warped-rivers module]: This turbulence module warps the output value
                // from the combined-rivers module, which twists the rivers.  The high
                // roughness produces less-smooth rivers.
                Source0 = new Turbulence
                {
                    Seed = Settings.Seed + 102,
                    Frequency = 9.25,
                    Power = 1.0 / 57.75,
                    Roughness = 4,
                    // [Combined-rivers module]: This minimum-value module causes the small
                    // rivers to cut into the large rivers.  It does this by selecting the
                    // minimum output values from the large-river-curve module and the small-
                    // river-curve module.
                    Source0 = new Min
                    {
                        // [Large-river-curve module]: This curve module applies a curve to the
                        // output value from the large-river-basis module so that the ridges
                        // become inverted.  This creates the rivers.  This curve also compresses
                        // the edge of the rivers, producing a sharp transition from the land to
                        // the river bottom.
                        Source0 = new Curve
                        {
                            ControlPoints = new List<Curve.ControlPoint>
                            {
                                new Curve.ControlPoint(-2.0000, -2.0000),
                                new Curve.ControlPoint(-1.0000, -1.2500),
                                new Curve.ControlPoint(-0.0000, -0.7500),
                                new Curve.ControlPoint( 0.5000, -0.2500),
                                new Curve.ControlPoint( 0.6250,  0.8750),
                                new Curve.ControlPoint( 0.7500,  1.0000),
                                new Curve.ControlPoint( 2.0000,  1.2500),
                            },
                            // [Large-river-basis module]: This ridged-multifractal-noise module
                            // creates the large, deep rivers.
                            Source0 = new RidgedMulti
                            {
                                Seed = Settings.Seed + 100,
                                Frequency = 18.75,
                                Lacunarity = Settings.ContinentLacunarity,
                                OctaveCount = 1,
                                Quality = NoiseQuality.Best,
                            },
                        },
                        // [Small-river-curve module]: This curve module applies a curve to the
                        // output value from the small-river-basis module so that the ridges
                        // become inverted.  This creates the rivers.  This curve also compresses
                        // the edge of the rivers, producing a sharp transition from the land to
                        // the river bottom.
                        Source1 = new Curve
                        {
                            ControlPoints = new List<Curve.ControlPoint>
                            {
                                new Curve.ControlPoint(-2.0000, -2.0000),
                                new Curve.ControlPoint(-1.0000, -1.2500),
                                new Curve.ControlPoint(-0.0000, -0.7500),
                                new Curve.ControlPoint( 0.5000, -0.2500),
                                new Curve.ControlPoint( 0.6250,  0.8750),
                                new Curve.ControlPoint( 0.7500,  1.0000),
                                new Curve.ControlPoint( 2.0000,  1.2500),
                            },
                            // [Small-river-basis module]: This ridged-multifractal-noise module
                            // creates the small, shallow rivers.
                            Source0 = new RidgedMulti
                            {
                                Seed = Settings.Seed + 101,
                                Frequency = 43.25,
                                Lacunarity = Settings.ContinentLacunarity,
                                OctaveCount = 1,
                                Quality = NoiseQuality.Best,
                            },
                        },
                    },
                },
            };

            return riverPositions;
        }

        Module CreateScaledMountainousTerrain(Module mountainousTerrain)
        {
            // Scales the output value from the mountainous-terrain group
            // so that it can be added to the elevation defined by the continent-
            // definition group.
            //
            // This subgroup scales the output value such that it is almost always
            // positive.  This is done so that a negative elevation does not get applied
            // to the continent-definition group, preventing parts of that group from
            // having negative terrain features "stamped" into it.
            //
            // The output value from this module subgroup is measured in planetary
            // elevation units (-1.0 for the lowest underwater trenches and +1.0 for the
            // highest mountain peaks.)
            //
            // [Scaled-mountainous-terrain group]: Caches the output value from the
            // peak-height-multiplier module.  This is the output value for the
            // entire scaled-mountainous-terrain group.
            var scaledMountainousTerrain = new Cache
            {
                // [Peak-height-multiplier module]: This multiplier module modulates the
                // heights of the mountain peaks from the base-scaled-mountainous-terrain
                // module using the output value from the scaled-peak-modulation module.
                Source0 = new Multiply
                {
                    // [Base-scaled-mountainous-terrain module]: This scale/bias module
                    // scales the output value from the mountainous-terrain group so that the
                    // output value is measured in planetary elevation units.
                    Source0 = new ScaleBias
                    {
                        Scale = 0.125,
                        Bias = 0.125,
                        Source0 = mountainousTerrain,
                    },
                    // [Scaled-peak-modulation module]: This scale/bias module modifies the
                    // range of the output value from the peak-modulation module so that it
                    // can be used as the modulator for the peak-height-multiplier module.
                    // It is important that this output value is not much lower than 1.0.
                    Source1 = new ScaleBias
                    {
                        Scale = 0.25,
                        Bias = 1,
                        // [Peak-modulation module]: This exponential-curve module applies an
                        // exponential curve to the output value from the base-peak-modulation
                        // module.  This produces a small number of high values and a much larger
                        // number of low values.  This means there will be a few peaks with much
                        // higher elevations than the majority of the peaks, making the terrain
                        // features more varied.
                        Source0 = new Exponent
                        {
                            Exp = 1.25,
                            // [Base-peak-modulation module]: At this stage, most mountain peaks have
                            // roughly the same elevation.  This Perlin-noise module generates some
                            // random values that will be used by subsequent noise modules to
                            // randomly change the elevations of the mountain peaks.
                            Source0 = new Perlin
                            {
                                Seed = Settings.Seed + 110,
                                Frequency = 14.5,
                                Persistence = 0.5,
                                Lacunarity = Settings.MountainLacunarity,
                                OctaveCount = 3,
                                Quality = NoiseQuality.Standard,
                            },
                        },
                    },
                },
            };

            return scaledMountainousTerrain;
        }

        Module CreateScaledHillyTerrain(Module hillyTerrain)
        {
            // Scales the output value from the hilly-terrain group so
            // that it can be added to the elevation defined by the continent-
            // definition group.  The scaling amount applied to the hills is one half of
            // the scaling amount applied to the scaled-mountainous-terrain group.
            //
            // This subgroup scales the output value such that it is almost always
            // positive.  This is done so that negative elevations are not applied to
            // the continent-definition group, preventing parts of the continent-
            // definition group from having negative terrain features "stamped" into it.
            //
            // The output value from this module subgroup is measured in planetary
            // elevation units (-1.0 for the lowest underwater trenches and +1.0 for the
            // highest mountain peaks.)
            //
            // [Scaled-hilly-terrain group]: Caches the output value from the
            // hilltop-height-multiplier module.  This is the output value for the
            // entire scaled-hilly-terrain group.
            var scaledHillyTerrain = new Cache
            {
                // [Hilltop-height-multiplier module]: This multiplier module modulates
                // the heights of the hilltops from the base-scaled-hilly-terrain module
                // using the output value from the scaled-hilltop-modulation module.
                Source0 = new Multiply
                {
                    // [Base-scaled-hilly-terrain module]: This scale/bias module scales the
                    // output value from the hilly-terrain group so that this output value is
                    // measured in planetary elevation units 
                    Source0 = new ScaleBias
                    {
                        Scale = 0.0625,
                        Bias = 0.0625,
                        Source0 = hillyTerrain,
                    },
                    // [Scaled-hilltop-modulation module]: This scale/bias module modifies
                    // the range of the output value from the hilltop-modulation module so
                    // that it can be used as the modulator for the hilltop-height-multiplier
                    // module.  It is important that this output value is not much lower than
                    // 1.0.
                    Source1 = new ScaleBias
                    {
                        Scale = 0.5,
                        Bias = 1.5,
                        // [Hilltop-modulation module]: This exponential-curve module applies an
                        // exponential curve to the output value from the base-hilltop-modulation
                        // module.  This produces a small number of high values and a much larger
                        // number of low values.  This means there will be a few hilltops with
                        // much higher elevations than the majority of the hilltops, making the
                        // terrain features more varied.
                        Source0 = new Exponent
                        {
                            Exp = 1.25,
                            // [Base-hilltop-modulation module]: At this stage, most hilltops have
                            // roughly the same elevation.  This Perlin-noise module generates some
                            // random values that will be used by subsequent noise modules to
                            // randomly change the elevations of the hilltops.
                            Source0 = new Perlin
                            {
                                Seed = Settings.Seed + 120,
                                Frequency = 13.5,
                                Persistence = 0.5,
                                Lacunarity = Settings.HillsLacunarity,
                                OctaveCount = 3,
                                Quality = NoiseQuality.Standard,
                            }
                        },
                    },
                },
            };

            return scaledHillyTerrain;
        }

        Module CreateScaledPlainsTerrain(Module plainsTerrain)
        {
            // Scales the output value from the plains-terrain group so
            // that it can be added to the elevations defined by the continent-
            // definition group.
            //
            // This subgroup scales the output value such that it is almost always
            // positive.  This is done so that negative elevations are not applied to
            // the continent-definition group, preventing parts of the continent-
            // definition group from having negative terrain features "stamped" into it.
            //
            // The output value from this module subgroup is measured in planetary
            // elevation units (-1.0 for the lowest underwater trenches and +1.0 for the
            // highest mountain peaks.)
            //
            // [Scaled-plains-terrain group]: Caches the output value from the
            // scaled-plains-terrain module.  This is the output value for the entire
            // scaled-plains-terrain group.
            var scaledPlainsTerrain = new Cache
            {
                // [Scaled-plains-terrain module]: This scale/bias module greatly
                // flattens the output value from the plains terrain.  This output value
                // is measured in planetary elevation units 
                Source0 = new ScaleBias
                {
                    Scale = 0.00390625,
                    Bias = 0.0078125,
                    Source0 = plainsTerrain,
                },
            };

            return scaledPlainsTerrain;
        }

        Module CreateScaledBadlandsTerrain(Module badlandsTerrain)
        {
            // Scales the output value from the badlands-terrain group so
            // that it can be added to the elevations defined by the continent-
            // definition group.
            //
            // This subgroup scales the output value such that it is almost always
            // positive.  This is done so that negative elevations are not applied to
            // the continent-definition group, preventing parts of the continent-
            // definition group from having negative terrain features "stamped" into it.
            //
            // The output value from this module subgroup is measured in planetary
            // elevation units (-1.0 for the lowest underwater trenches and +1.0 for the
            // highest mountain peaks.)
            //
            // [Scaled-badlands-terrain group]: Caches the output value from the
            // scaled-badlands-terrain module.  This is the output value for the
            // entire scaled-badlands-terrain group.
            var scaledBadlandsTerrain = new Cache
            {
                // [Scaled-badlands-terrain module]: This scale/bias module scales the
                // output value from the badlands-terrain group so that it is measured
                // in planetary elevation units 
                Source0 = new ScaleBias
                {
                    Scale = 0.0625,
                    Bias = 0.0625,
                    Source0 = badlandsTerrain,
                },
            };

            return scaledBadlandsTerrain;
        }

        Module CreateFinalPlanet(Module continentDefinition, Module terrainTypeDefinition,
            Module scaledPlainsTerrain, Module scaledHillyTerrain, Module scaledMountainousTerrain,
            Module scaledBadlandsTerrain, Module riverPositions)
        {
            // Creates the continental shelves.
            //
            // The output value from this module subgroup are measured in planetary
            // elevation units (-1.0 for the lowest underwater trenches and +1.0 for the
            // highest mountain peaks.)
            //
            // [Continental-shelf subgroup]: Caches the output value from the shelf-
            // and-trenches module.
            var continentalShelf = new Cache
            {
                // [Shelf-and-trenches module]: This addition module adds the oceanic
                // trenches to the clamped-sea-bottom module.
                Source0 = new Add
                {
                    // [Oceanic-trench module]: This scale/bias module inverts the ridges
                    // from the oceanic-trench-basis-module so that the ridges become
                    // trenches.  This noise module also reduces the depth of the trenches so
                    // that their depths are measured in planetary elevation units.
                    Source0 = new ScaleBias
                    {
                        Scale = -0.125,
                        Bias = -0.125,
                        // [Shelf-creator module]: This terracing module applies a terracing
                        // curve to the continent-definition group at the specified shelf level.
                        // This terrace becomes the continental shelf.  Note that this terracing
                        // module also places another terrace below the continental shelf near
                        // -1.0.  The bottom of this terrace is defined as the bottom of the
                        // ocean; subsequent noise modules will later add oceanic trenches to the
                        // bottom of the ocean.
                        Source0 = new Terrace
                        {
                            ControlPoints = new List<double>
                            {
                                -1.0,
                                -0.75,
                                -0.375,
                                1.0,
                            },
                            Source0 = continentDefinition,
                        },
                    },
                    // [Clamped-sea-bottom module]: This clamping module clamps the output
                    // value from the shelf-creator module so that its possible range is
                    // from the bottom of the ocean to sea level.  This is done because this
                    // subgroup is only concerned about the oceans.
                    Source1 = new Clamp
                    {
                        LowerBound = -0.75,
                        UpperBound = Settings.SeaLevel,
                        // [Oceanic-trench-basis module]: This ridged-multifractal-noise module
                        // generates some coherent noise that will be used to generate the
                        // oceanic trenches.  The ridges represent the bottom of the trenches.
                        Source0 = new RidgedMulti
                        {
                            Seed = Settings.Seed + 130,
                            Frequency = Settings.ContinentFrequency * 4.375,
                            Lacunarity = Settings.ContinentLacunarity,
                            OctaveCount = 16,
                            Quality = NoiseQuality.Best,
                        }
                    },
                }
            };

            // Generates the base elevations for the continents, before
            // terrain features are added.
            //
            // The output value from this module subgroup is measured in planetary
            // elevation units (-1.0 for the lowest underwater trenches and +1.0 for the
            // highest mountain peaks.)
            //
            // [Base-continent-elevation subgroup]: Caches the output value from the
            // base-continent-with-oceans module.
            var baseContinentElevation = new Cache
            {
                // [Base-continent-with-oceans module]: This selector module applies the
                // elevations of the continental shelves to the base elevations of the
                // continent.  It does this by selecting the output value from the
                // continental-shelf subgroup if the corresponding output value from the
                // continent-definition group is below the shelf level.  Otherwise, it
                // selects the output value from the base-scaled-continent-elevations
                // module.
                Source0 = new Select
                {
                    LowerBound = 0 - 1000,
                    UpperBound = 0,
                    EdgeFalloff = 0.03125,
                    // [Base-scaled-continent-elevations module]: This scale/bias module
                    // scales the output value from the continent-definition group so that it
                    // is measured in planetary elevation units 
                    Source0 = new ScaleBias
                    {
                        Scale = Settings.ContinentHeightScale,
                        Bias = 0,
                        Source0 = continentDefinition,
                    },
                    Source1 = continentalShelf,
                    Control = continentDefinition,
                }
            };

            // Applies the scaled-plains-terrain group to the base-
            // continent-elevation subgroup.
            //
            // The output value from this module subgroup is measured in planetary
            // elevation units (-1.0 for the lowest underwater trenches and +1.0 for the
            // highest mountain peaks.)
            //
            // [Continents-with-plains subgroup]: Caches the output value from the
            // continents-with-plains module.
            var continentsWithPlains = new Cache
            {
                // [Continents-with-plains module]:  This addition module adds the
                // scaled-plains-terrain group to the base-continent-elevation subgroup.
                Source0 = new Add
                {
                    Source0 = baseContinentElevation,
                    Source1 = scaledPlainsTerrain,
                },
            };

            // Applies the scaled-hilly-terrain group to the continents-
            // with-plains subgroup.
            //
            // The output value from this module subgroup is measured in planetary
            // elevation units (-1.0 for the lowest underwater trenches and +1.0 for the
            // highest mountain peaks.)
            //
            // [Continents-with-hills subgroup]: Caches the output value from the
            // select-high-elevations module.
            var continentsWithHills = new Cache
            {
                // [Select-high-elevations module]: This selector module ensures that
                // the hills only appear at higher elevations.  It does this by selecting
                // the output value from the continent-with-hills module if the
                // corresponding output value from the terrain-type-defintion group is
                // above a certain value. Otherwise, it selects the output value from the
                // continents-with-plains subgroup.
                Source0 = new Select
                {
                    LowerBound = 1 - Settings.HillsAmount,
                    UpperBound = 1001 - Settings.HillsAmount,
                    EdgeFalloff = 0.25,
                    Source0 = continentsWithPlains,
                    // [Continents-with-hills module]:  This addition module adds the scaled-
                    // hilly-terrain group to the base-continent-elevation subgroup.
                    Source1 = new Add
                    {
                        Source0 = baseContinentElevation,
                        Source1 = scaledHillyTerrain,
                    },
                    Control = terrainTypeDefinition,
                },
            };

            // Applies the scaled-mountainous-terrain group to the
            // continents-with-hills subgroup.
            //
            // The output value from this module subgroup is measured in planetary
            // elevation units (-1.0 for the lowest underwater trenches and +1.0 for the
            // highest mountain peaks.)
            //
            // [Continents-with-mountains subgroup]: Caches the output value from
            // the select-high-elevations module.
            var continentsWithMountains = new Cache
            {
                // [Select-high-elevations module]: This selector module ensures that
                // mountains only appear at higher elevations.  It does this by selecting
                // the output value from the continent-with-mountains module if the
                // corresponding output value from the terrain-type-defintion group is
                // above a certain value.  Otherwise, it selects the output value from
                // the continents-with-hills subgroup.  Note that the continents-with-
                // hills subgroup also contains the plains terrain.
                Source0 = new Select
                {
                    LowerBound = 1 - Settings.MountainsAmount,
                    UpperBound = 1001 - Settings.MountainsAmount,
                    EdgeFalloff = 0.25,
                    Source0 = continentsWithHills,
                    // [Add-increased-mountain-heights module]: This addition module adds
                    // the increased-mountain-heights module to the continents-and-
                    // mountains module.  The highest continent elevations now have the
                    // highest mountains.
                    Source1 = new Add
                    {
                        // [Continents-and-mountains module]:  This addition module adds the
                        // scaled-mountainous-terrain group to the base-continent-elevation
                        // subgroup.
                        Source0 = new Add
                        {
                            Source0 = baseContinentElevation,
                            Source1 = scaledMountainousTerrain,
                        },
                        // [Increase-mountain-heights module]:  This curve module applies a curve
                        // to the output value from the continent-definition group.  This
                        // modified output value is used by a subsequent noise module to add
                        // additional height to the mountains based on the current continent
                        // elevation.  The higher the continent elevation, the higher the
                        // mountains.
                        Source1 = new Curve
                        {
                            ControlPoints = new List<Curve.ControlPoint>
                            {
                                new Curve.ControlPoint (-1.0, -0.0625),
                                new Curve.ControlPoint ( 0.0,  0.0000),
                                new Curve.ControlPoint ( 1.0 - Settings.MountainsAmount,  0.0625),
                                new Curve.ControlPoint ( 1.0,  0.2500),
                            },
                            Source0 = continentDefinition,
                        },
                    },
                    Control = terrainTypeDefinition,
                },
            };

            // Applies the scaled-badlands-terrain group to the
            // continents-with-mountains subgroup.
            //
            // The output value from this module subgroup is measured in planetary
            // elevation units (-1.0 for the lowest underwater trenches and +1.0 for the
            // highest mountain peaks.)
            //
            // [Continents-with-badlands subgroup]: Caches the output value from the
            // apply-badlands module.
            var continentsWithBadlands = new Cache
            {
                // [Apply-badlands module]: This maximum-value module causes the badlands
                // to "poke out" from the rest of the terrain.  It does this by ensuring
                // that only the maximum of the output values from the continents-with-
                // mountains subgroup and the select-badlands-positions modules
                // contribute to the output value of this subgroup.  One side effect of
                // this process is that the badlands will not appear in mountainous
                // terrain.
                Source0 = new Max
                {
                    Source0 = continentsWithMountains,
                    // [Select-badlands-positions module]: This selector module places
                    // badlands at random spots on the continents based on the Perlin noise
                    // generated by the badlands-positions module.  To do this, it selects
                    // the output value from the continents-and-badlands module if the
                    // corresponding output value from the badlands-position module is
                    // greater than a specified value.  Otherwise, this selector module
                    // selects the output value from the continents-with-mountains subgroup.
                    // There is also a wide transition between these two noise modules so
                    // that the badlands can blend into the rest of the terrain on the
                    // continents.
                    Source1 = new Select
                    {
                        LowerBound = 1 - Settings.BadlandsAmount,
                        UpperBound = 1001 - Settings.BadlandsAmount,
                        EdgeFalloff = 0.25,
                        Source0 = continentsWithMountains,
                        // [Continents-and-badlands module]:  This addition module adds the
                        // scaled-badlands-terrain group to the base-continent-elevation
                        // subgroup.
                        Source1 = new Add
                        {
                            Source0 = baseContinentElevation,
                            Source1 = scaledBadlandsTerrain,
                        },
                        // [Badlands-positions module]: This Perlin-noise module generates some
                        // random noise, which is used by subsequent noise modules to specify the
                        // locations of the badlands.
                        Control = new Perlin
                        {
                            Seed = Settings.Seed + 140,
                            Frequency = 16.5,
                            Persistence = 0.5,
                            Lacunarity = Settings.ContinentLacunarity,
                            OctaveCount = 2,
                            Quality = NoiseQuality.Standard,
                        },
                    },
                },
            };

            // Applies the river-positions group to the continents-with-
            // badlands subgroup.
            //
            // The output value from this module subgroup is measured in planetary
            // elevation units (-1.0 for the lowest underwater trenches and +1.0 for the
            // highest mountain peaks.)
            //
            // [Continents-with-rivers subgroup]: Caches the output value from the
            // blended-rivers-to-continents module.
            var continentsWithRivers = new Cache
            {
                // [Blended-rivers-to-continents module]: This selector module outputs
                // deep rivers near sea level and shallower rivers in higher terrain.  It
                // does this by selecting the output value from the continents-with-
                // badlands subgroup if the corresponding output value from the
                // continents-with-badlands subgroup is far from sea level.  Otherwise,
                // this selector module selects the output value from the add-rivers-to-
                // continents module.
                Source0 = new Select
                {
                    LowerBound = Settings.SeaLevel,
                    UpperBound = Settings.ContinentHeightScale + Settings.SeaLevel,
                    EdgeFalloff = Settings.ContinentHeightScale - Settings.SeaLevel,
                    Source0 = continentsWithBadlands,
                    // [Add-rivers-to-continents module]: This addition module adds the
                    // rivers to the continents-with-badlands subgroup.  Because the scaled-
                    // rivers module only outputs a negative value, the scaled-rivers module
                    // carves the rivers out of the terrain.
                    Source1 = new Add
                    {
                        Source0 = continentsWithBadlands,
                        // [Scaled-rivers module]: This scale/bias module scales the output value
                        // from the river-positions group so that it is measured in planetary
                        // elevation units and is negative; this is required for Add-rivers-to-continents.
                        Source1 = new ScaleBias
                        {
                            Scale = Settings.RiverDepth / 3.0,
                            Bias = -Settings.RiverDepth / 2.0,
                            Source0 = riverPositions,
                        },
                    },
                    Control = continentsWithBadlands,
                },
            };


            // Simply caches the output value from the continent-with-
            // rivers subgroup to contribute to the final output value.
            //
            // [Unscaled-final-planet subgroup]: Caches the output value from the
            // continent-with-rivers subgroup.
            var unscaledFinalPlanet = new Cache
            {
                Source0 = continentsWithRivers,
            };

            // Scales the output value from the unscaled-final-planet
            // subgroup so that it represents an elevation in meters.
            //
            // 2: [Final-planet group]: Caches the output value from the final-planet-
            // in-meters module.
            var finalPlanet = new Cache
            {
                // [Final-planet-in-meters module]: This scale/bias module scales the
                // output value from the unscaled-final-planet subgroup so that its
                // output value is measured in meters.
                Source0 = new ScaleBias
                {
                    Scale = (Settings.MaxElev - Settings.MinElev) / 2.0,
                    Bias = Settings.MinElev + ((Settings.MaxElev - Settings.MinElev) / 2.0),
                    Source0 = unscaledFinalPlanet,
                },
            };

            return finalPlanet;
        }

        #endregion

        public Module CreateModule()
        {
            var continentDefinition = CreateContinentDefinition();
            var terrainTypeDefinition = CreateTerrainTypeDefinition(continentDefinition);
            var scaledPlainsTerrain = CreateScaledPlainsTerrain(CreatePlainsTerrain());
            var scaledHillyTerrain = CreateScaledHillyTerrain(CreateHillyTerrain());
            var scaledMountainousTerrain = CreateScaledMountainousTerrain(CreateMountainousTerrain());
            var scaledbadlandsTerrain = CreateScaledBadlandsTerrain(CreateBadlandsTerrain());
            RiverPositions = CreateRiverPositions();
            var planet = CreateFinalPlanet(continentDefinition, terrainTypeDefinition,
                scaledPlainsTerrain, scaledHillyTerrain, scaledMountainousTerrain,
                scaledbadlandsTerrain, RiverPositions);
            return planet;
        }
    }
}
