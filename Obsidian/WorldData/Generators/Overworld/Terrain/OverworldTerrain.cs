using SharpNoise;
using SharpNoise.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.WorldData.Generators.Overworld.Terrain
{
    public class OverworldTerrain
    {
        private Module Result { get; set; }

        private readonly OverworldTerrainSettings settings;

        private readonly BaseTerrain badlands, plains, hills;

        private readonly Module baseContinents;

        public OverworldTerrain(OverworldTerrainSettings ots)
        {
            settings = ots;
            Cache unscaledPlanet = new Cache();
            plains = new PlainsTerrain(ots);
            hills = new HillsTerrain(ots);
            badlands = new BadlandsTerrain(ots);

            var contDef = ContinentalDefinition();
            var terDef = TerrainTypeDefinition(contDef);


            baseContinents = BaseContinents();




            unscaledPlanet = new Cache
            {
                Source0 = TerrainTypeDefinition(contDef)
            };

            // Scale bias scales the output (usually -1.0 to +1.0) to
            // Minecraft values. If MinElev is 40 (leaving room for caves under oceans)
            // and MaxElev is 168, a value of -1 becomes 40, and a value of 1 becomes 168.
            Result = new ScaleBias
            {
                Scale = (settings.MaxElev - settings.MinElev) / 2.0,
                Bias = settings.MinElev + ((settings.MaxElev - settings.MinElev) / 2.0),
                Source0 = unscaledPlanet,
            };
        }

        public double GetValue(double x, double z, double y = 0)
        {
            return Result.GetValue(x*0.01f, y, z*0.01f);
        }

        private Module Continents()
        {
            return null;
        }

        // Generates the river positions.
        //
        // -1.0 represents the lowest elevations and +1.0 represents the highest
        // elevations.
        //
        // [River-positions group]: Caches the output value from the warped-
        // rivers module.  This is the output value for the entire river-
        // positions group.
        private Module Rivers()
        {
            return new Cache
            {
                // [Warped-rivers module]: This turbulence module warps the output value
                // from the combined-rivers module, which twists the rivers.  The high
                // roughness produces less-smooth rivers.
                Source0 = new Clamp
                {
                    Source0 = new Turbulence
                    {
                        Seed = settings.Seed + 102,
                        Frequency = 3.25,
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
                                new Curve.ControlPoint(-2.000,  2.0000),
                                new Curve.ControlPoint(-0.095,  1.5000),
                                new Curve.ControlPoint( 0.000,  -2.0000),
                                new Curve.ControlPoint( 0.095,  1.5000),
                                new Curve.ControlPoint( 2.000,  2.0000),
                            },
                                // [Large-river-basis module]: This ridged-multifractal-noise module
                                // creates the large, deep rivers.
                                Source0 = new RidgedMulti
                                {
                                    Seed = settings.Seed + 100,
                                    Frequency = 1.25,
                                    Lacunarity = settings.ContinentLacunarity,
                                    OctaveCount = 3,
                                    Quality = NoiseQuality.Fast,
                                },
                            },
                            // [Small-river-curve module]: This curve module applies a curve to the
                            // output value from the small-river-basis module so that the ridges
                            // become inverted.  This creates the rivers.  This curve also compresses
                            // the edge of the rivers, producing a sharp transition from the land to
                            // the river bottom.
                            Source1 = new Constant { ConstantValue = 2 }
                        }
                    }
                }
            };
        }
       

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
        private Module TerrainTypeDefinition(Module continentDefinition)
        {
            return new Cache
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
                        settings.ShelfLevel + settings.SeaLevel / 2.0,
                        1,
                    },
                    // [Warped-continent module]: This turbulence module slightly warps the
                    // output value from the continent-definition group.  This prevents the
                    // rougher terrain from appearing exclusively at higher elevations.
                    // Rough areas may now appear in the the ocean, creating rocky islands
                    // and fjords.
                    Source0 = new Turbulence
                    {
                        Seed = settings.Seed + 20,
                        Frequency = settings.ContinentFrequency * 1.125,
                        Power = settings.ContinentFrequency / 20.59375 * settings.TerrainOffset,
                        Roughness = 3,
                        Source0 = continentDefinition,
                    }
                }
            };
        }

        private Module MergeTerrain()
        {
            var continentsWithPlains = new Cache
            {
                // [Continents-with-plains module]:  This addition module adds the
                // scaled-plains-terrain group to the base-continent-elevation subgroup.
                Source0 = new Add
                {
                    Source0 = baseContinents,
                    Source1 = plains.Result
                }
            };

            return null;
        }

        // Generates the base elevations for the continents, before
        // terrain features are added.
        //
        // The output value from this module subgroup is measured in planetary
        // elevation units (-1.0 for the lowest underwater trenches and +1.0 for the
        // highest mountain peaks.)
        //
        // [Base-continent-elevation subgroup]: Caches the output value from the
        // base-continent-with-oceans module.
        private Module BaseContinents()
        {
            return new Cache
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
                        Scale = settings.ContinentHeightScale,
                        Bias = 0,
                        Source0 = ContinentalDefinition(),
                    },
                    Source1 = ContinentalShelves(),
                    Control = ContinentalDefinition(),
                }
            };
        }

        private Module ContinentalShelves()
        {
            return new Cache
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
                            Source0 = ContinentalDefinition(),
                        },
                    },
                    // [Clamped-sea-bottom module]: This clamping module clamps the output
                    // value from the shelf-creator module so that its possible range is
                    // from the bottom of the ocean to sea level.  This is done because this
                    // subgroup is only concerned about the oceans.
                    Source1 = new Clamp
                    {
                        LowerBound = -0.75,
                        UpperBound = settings.SeaLevel,
                        // [Oceanic-trench-basis module]: This ridged-multifractal-noise module
                        // generates some coherent noise that will be used to generate the
                        // oceanic trenches.  The ridges represent the bottom of the trenches.
                        Source0 = new RidgedMulti
                        {
                            Seed = settings.Seed + 130,
                            Frequency = settings.ContinentFrequency / 4.375,
                            Lacunarity = settings.ContinentLacunarity,
                            OctaveCount = 16,
                            Quality = NoiseQuality.Fast,
                        }
                    },
                }
            };
        }

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
        private Module ContinentalDefinition()
        {
            return new Cache
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
                                Seed = settings.Seed + 1,
                                Frequency = settings.ContinentFrequency*0.5,
                                Persistence = 0.5,
                                Lacunarity = settings.ContinentLacunarity,
                                OctaveCount = 11,
                                Quality = NoiseQuality.Fast,
                            },
                        },
                        // [Continent-with-ranges module]: Next, a curve module modifies the
                        // output value from the continent module so that very high values appear
                        // near sea level.  This defines the positions of the mountain ranges.
                        Source1 = new Curve
                        {
                            ControlPoints = new List<Curve.ControlPoint>
                            {
                                new Curve.ControlPoint(-2.0000 + settings.SeaLevel, -1.625 + settings.SeaLevel),
                                new Curve.ControlPoint(-1.0000 + settings.SeaLevel, -1.375 + settings.SeaLevel),
                                new Curve.ControlPoint( 0.0000 + settings.SeaLevel, -0.375 + settings.SeaLevel),
                                new Curve.ControlPoint( 0.0625 + settings.SeaLevel,  0.125 + settings.SeaLevel),
                                new Curve.ControlPoint( 0.1250 + settings.SeaLevel,  0.250 + settings.SeaLevel),
                                new Curve.ControlPoint( 0.2500 + settings.SeaLevel,  1.000 + settings.SeaLevel),
                                new Curve.ControlPoint( 0.5000 + settings.SeaLevel,  0.250 + settings.SeaLevel),
                                new Curve.ControlPoint( 0.7500 + settings.SeaLevel,  0.250 + settings.SeaLevel),
                                new Curve.ControlPoint( 1.0000 + settings.SeaLevel,  0.500 + settings.SeaLevel),
                                new Curve.ControlPoint( 2.0000 + settings.SeaLevel,  0.500 + settings.SeaLevel),
                            },
                            // [Continent module]: This Perlin-noise module generates the continents.
                            // This noise module has a high number of octaves so that detail is
                            // visible at high zoom levels.
                            Source0 = new Perlin
                            {
                                Seed = settings.Seed + 0,
                                Frequency = settings.ContinentFrequency*0.1,
                                Persistence = 0.5,
                                Lacunarity = settings.ContinentLacunarity,
                                OctaveCount = 14,
                                Quality = NoiseQuality.Fast,
                            },
                        },
                    },
                },
            };
        }
    }
}
