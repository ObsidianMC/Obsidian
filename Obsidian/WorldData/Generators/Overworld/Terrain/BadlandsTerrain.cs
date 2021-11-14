using SharpNoise;
using SharpNoise.Modules;
using System.Collections.Generic;

namespace Obsidian.WorldData.Generators.Overworld.Terrain;

public class BadlandsTerrain : BaseTerrain
{
    // Generates the hilly terrain.
    //
    // -1.0 represents the lowest elevations and +1.0 represents the highest
    // elevations.
    //
    // [Hilly-terrain group]: Caches the output value from the warped-hilly-
    // terrain module.  This is the output value for the entire hilly-
    // terrain group.
    public BadlandsTerrain() : base()
    {
        this.Result = new Cache
        {
            // Sanity check to force results b/w -1.0<y<1.0
            Source0 = new ScalePoint
            {
                XScale = 1 / 190.103,
                YScale = 1 / 140.103,
                ZScale = 1 / 190.103,
                Source0 = new Clamp
                {
                    Source0 = new Max
                    {
                        Source0 = BadlandsCliffs(),
                        // [Scaled-sand-dunes module]: This scale/bias module considerably
                        // flattens the output value from the badlands-sands subgroup and lowers
                        // this value to near -1.0.
                        Source1 = new ScaleBias
                        {
                            Scale = 0.02,
                            Bias = 0.04,
                            Source0 = BadlandsSands(),
                        },
                    }
                }
            }
        };
    }

    private Module BadlandsSands()
    {
        return new Cache
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
                        Seed = settings.Seed + 80,
                        Frequency = 9.13,
                        Lacunarity = settings.BadlandsLacunarity,
                        Quality = NoiseQuality.Fast,
                        OctaveCount = 1,
                    },
                },
                // [Scaled-dune-detail module]: This scale/bias module shrinks the dune
                // details by a large amount.  This is necessary so that the subsequent
                // noise modules in this subgroup can add this detail to the sand-dunes
                // module.
                Source1 = new ScaleBias
                {
                    Scale = 0.25,
                    Bias = 0.25,
                    // [Dune-detail module]: This noise module uses Voronoi polygons to
                    // generate the detail to add to the dunes.  By enabling the distance
                    // algorithm, small polygonal pits are generated; the edges of the pits
                    // are joined to the edges of nearby pits.
                    Source0 = new Cell
                    {
                        Seed = settings.Seed + 81,
                        Frequency = 11.1,
                        Displacement = 0,
                        EnableDistance = true,
                    },
                },
            },
        };
    }

    // Generates the cliffs for the badlands.
    //
    // -1.0 represents the lowest elevations and +1.0 represents the highest
    // elevations.
    //
    // [Badlands-cliffs subgroup]: Caches the output value from the warped-
    // cliffs module.
    private Module BadlandsCliffs()
    {
        return new Cache
        {
            // [Warped-cliffs module]: This turbulence module warps the output value
            // from the coarse-turbulence module.  This turbulence has a higher
            // frequency, but lower power, than the coarse-turbulence module, adding
            // some fine detail to it.
            Source0 = new Turbulence
            {
                Seed = settings.Seed + 92,
                Frequency = 4,
                Power = 1.0 / 211543.0 * settings.BadlandsTwist,
                Roughness = 2,
                // [Coarse-turbulence module]: This turbulence module warps the output
                // value from the terraced-cliffs module, adding some coarse detail to
                // it.
                Source0 = new Turbulence
                {
                    Seed = settings.Seed + 91,
                    Frequency = 6,
                    Power = 1.0 / 141539.0 * settings.BadlandsTwist,
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
                            UpperBound = 0.475,
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
                                    Seed = settings.Seed + 90,
                                    Frequency = 8,
                                    Persistence = 0.5,
                                    Lacunarity = settings.BadlandsLacunarity,
                                    OctaveCount = 6,
                                    Quality = NoiseQuality.Fast,
                                },
                            },
                        },
                    },
                },
            },
        };
    }
}
