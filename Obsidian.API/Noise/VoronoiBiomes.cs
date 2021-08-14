using SharpNoise;
using SharpNoise.Modules;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Obsidian.API.Noise
{
    public class VoronoiBiomes : Module
    {
        public int Seed { get; set; }

        public double Frequency { get; set; }

        internal enum BaseBiome : int
        {
            DeepOcean = -9, //unused methinks
            DeepFrozenOcean = -8,
            DeepColdOcean = -7,
            DeepLukewarmOcean = -6,
            DeepWarmOcean = -5,
            FrozenOcean = -4,
            ColdOcean = -3,
            LukewarmOcean = -2,
            WarmOcean = -1,
            Medium = 0,
            Cold = 1,
            Dry = 2, 
            Frozen = 3,
            MediumRare = 4,
            ColdRare = 5,
            DryRare = 6,
            FrozenRare = 7
        }

        internal readonly List<int> Oceans = new()
        {
            24, // Biomes.DeepOcean
            50, // Biomes.DeepFrozenOcean
            49, // Biomes.DeepColdOcean
            48, // Biomes.DeepLukewarmOcean
            47, // Biomes.DeepWarmOcean
            10, // Biomes.FrozenOcean
            46, // Biomes.ColdOcean
            45, // Biomes.LukewarmOcean
            44, // Biomes.WarmOcean
            0,  // Biomes.Ocean
        };

        public VoronoiBiomes() : base(0)
        {

        }

        public override double GetValue(double x, double y, double z)
        {
            x *= Frequency;
            z *= Frequency;
            

            var xint = (x > 0D) ? (int)x : (int)x - 1;
            var zint = (z > 0D) ? (int)z : (int)z - 1;

            Span<VoronoiCell> cells = stackalloc VoronoiCell[9];

            int index = 0;
            for (var zCur = zint - 1; zCur <= zint + 1; zCur++)
            {
                for (var xCur = xint - 1; xCur <= xint + 1; xCur++)
                {
                    var xPos = xCur + NoiseGenerator.ValueNoise3D(xCur, 0, zCur, Seed);
                    var zPos = zCur + NoiseGenerator.ValueNoise3D(xCur, 0, zCur, Seed + 2);
                    var xDist = xPos - x;
                    var zDist = zPos - z;
                    double dist = Math.Max(Math.Abs(xDist), Math.Abs(zDist));
                    var cell = new VoronoiCell
                    {
                        Index = (xCur, zCur),
                        DistanceToPoint = dist,
                        Point = (xPos, zPos),
                        // Wasteful to calculate below for all cells right now.
                        // Calculate cells we care about later.
                        BaseBiome = 0.0,
                        Biome = 0
                    };

                    cells[index++] = cell;
                }
            }

            MemoryExtensions.Sort(cells, (a, b) => { return a.DistanceToPoint > b.DistanceToPoint ? 1 : -1; });
            var meVal = NoiseGenerator.ValueNoise3D((int)Math.Floor(cells[0].Point.x), 0, (int)Math.Floor(cells[0].Point.z));
            (cells[0].BaseBiome, cells[0].Variant) = GetBaseBiome(meVal);
            var nearestVal = NoiseGenerator.ValueNoise3D((int)Math.Floor(cells[1].Point.x), 0, (int)Math.Floor(cells[1].Point.z));
            (cells[1].BaseBiome, cells[1].Variant) = GetBaseBiome(nearestVal);


            return (double)ProcessBiomeRules(cells[0], cells[1]);
        }

        private int ProcessVariants(VoronoiCell me, double averageDistance)
        {
            if ((int)me.BaseBiome < 0) // if ocean
            {
                // 65% of the ocean will be deep.
                if (me.DistanceToPoint < averageDistance * 0.65)
                {
                    me.BaseBiome -= 4; // deep varient
                }

                // 5% chance that a warm ocean has a mooshroom island.
                if (me.BaseBiome == BaseBiome.DeepWarmOcean && me.Variant > 5 && me.Variant < 10)
                {
                    if (me.DistanceToPoint < averageDistance * 0.15)
                    {
                        return 14; // Biomes.MushroomFields
                    }
                    else if (me.DistanceToPoint < averageDistance * 0.19)
                    {
                        return 15; // Biomes.MushroomFieldShore
                    }
                }

                // Map base biomes to proper Biomes values
                return me.BaseBiome switch
                {
                    BaseBiome.DeepOcean => 24, // Biomes.DeepOcean
                    BaseBiome.DeepFrozenOcean => 50, // Biomes.DeepFrozenOcean
                    BaseBiome.DeepColdOcean => 49, // Biomes.DeepColdOcean
                    BaseBiome.DeepLukewarmOcean => 48, // Biomes.DeepLukewarmOcean
                    BaseBiome.DeepWarmOcean => 47, // Biomes.DeepWarmOcean
                    BaseBiome.FrozenOcean => 10, // Biomes.FrozenOcean
                    BaseBiome.ColdOcean => 46, // Biomes.ColdOcean
                    BaseBiome.LukewarmOcean => 45, // Biomes.LukewarmOcean
                    BaseBiome.WarmOcean => 44, // Biomes.WarmOcean
                    _ => 0 //Biomes.Ocean
                };
            }
            else // if land
            {
                switch (me.BaseBiome)
                {
                    case BaseBiome.Dry:
                        {
                            if (me.Variant < 50)
                                return 2; // Biomes.Desert
                            if (me.Variant < 83)
                                return 35; // Biomes.Savanna
                            else
                                return 1; // Biomes.Plains
                        }
                    case BaseBiome.DryRare:
                        {
                            if (me.Variant < 33)
                                return 39; // Biomes.BadlandsPlateau
                            else
                                return 39; // Biomes.WoodedBadlandsPlateau
                        }
                    case BaseBiome.Medium:
                        {
                            if (me.Variant < 20)
                                return 4; // Biomes.Forest
                            if (me.Variant < 40)
                                return 29; // Biomes.DarkForest
                            if (me.Variant < 60)
                                return 27; // Biomes.BirchForest
                            if (me.Variant < 80)
                                return 3; // Biomes.Mountains
                            if (me.Variant < 90)
                                return 6; // Biomes.Swamp
                            else
                                return 1; // Biomes.Plains
                        }
                    case BaseBiome.MediumRare:
                        {
                            return 21; // Biomes.Jungle
                        }
                    case BaseBiome.Cold:
                        {
                            if (me.Variant < 25)
                                return 160; // Biomes.GiantSpruceTaiga
                            if (me.Variant < 50)
                                return 3; // Biomes.Mountains
                            if (me.Variant < 75)
                                return 5; // Biomes.Taiga
                            else
                                return 1; // Biomes.Plains
                        }
                    case BaseBiome.ColdRare:
                        {
                            return 32; // Biomes.GiantTreeTaiga
                        }
                    case BaseBiome.Frozen:
                        {
                            if (me.Variant < 75)
                                return 12; // Biomes.SnowyTundra
                            else
                                return 30; // Biomes.SnowyTaiga
                        }
                    case BaseBiome.FrozenRare:
                        {
                            return 1; // There are no rare frozen biomes (yet?)
                        }
                    default:
                        return 1; // Biomes.Plains
                }
            }
        }

        private VoronoiCell ProcessNeighborRules(VoronoiCell me, VoronoiCell neighbor, double averageDistance)
        {
            // Only run neighbor logic for outter 20% of a biome
            // Should save a lot of CPU cycles if we bail out now.
            if (me.DistanceToPoint < averageDistance * 0.80) { return me; }

            // Abandon hope, all ye who enter here
            switch (me.Biome)
            {
                // If a frozen or cold ocean borders a warm ocean,
                // then place a lukewarm ocean at the edge.
                case 10: // Biomes.FrozenOcean
                case 50: // Biomes.DeepFrozenOcean
                case 46: // Biomes.ColdOcean
                case 49: // Biomes.DeepColdOcean
                    if (neighbor.Biome == 44 || // Biomes.WarmOcean
                        neighbor.Biome == 47) // Biomes.DeepWarmOcean
                        me.Biome = 45; // Biomes.LukeWarmOcean
                    return me;

                // Badlands plateau and wooded badlands plateau generate
                // regular badlands on all edges.
                case 38: // Biomes.WoodedBadlandsPlateau
                case 39: // Biomes.BadlandsPlateau
                    me.Biome = 37; // Biomes.Badlands
                    return me;

                // Giant tree taiga generates the regular taiga on all edges,
                // unless there is a pre-existing snowy Taiga or taiga bordering it.
                case 32: // Biomes.GiantTreeTaiga
                    if (neighbor.Biome != 30) // Biomes.SnowyTaiga
                        me.Biome = 5; // Biomes.Taiga
                    return me;
                // If a desert borders a snowy tundra, a wooded mountain generates.
                case 2: // Biomes.Desert
                    if (neighbor.Biome == 12) // Biomes.SnowyTundra
                        me.Biome = 34; // Biomes.WoodedMountains
                    return me;
                // If a swamp borders a jungle, a jungle edge generate.
                // If a swamp borders a desert, snowy taiga, or snowy tundra,
                // a plains biome generates.
                case 6: // Biomes.Swamp
                    if (neighbor.Biome == 21) // Biomes.Jungle
                        me.Biome = 23; // Biomes.JungleEdge
                    else if (neighbor.Biome == 2 || // Biomes.Desert
                        neighbor.Biome == 30 || // Biomes.SnowyTaiga
                        neighbor.Biome == 12) // Biomes.SnowyTundra
                        me.Biome = 1; // Biomes.Plains
                    return me;
                default:
                    return me;
            }
        }

        private VoronoiCell ProcessBiomeCenterRules(VoronoiCell me, double averageDistance)
        {
            if (me.DistanceToPoint > averageDistance * 0.40) { return me; }

            me.Biome = me.Biome switch
            {
                1 => me.Variant < 30 ? 129 : me.Variant < 60 ? 18 : 4, // Biomes.Plains => Biomes.SunflowerPlains, Biomes.WoodedHills, Biomes.Forest,
                2 => 17, // Biomes.Desert => Biomes.DesertHills,
                3 => me.BaseBiome == BaseBiome.Cold ? 13 : 131, // Biomes.SnowyMountains : Biomes.GravellyMountains
                4 => me.BaseBiome == BaseBiome.Cold ? 18 : 132, // Biomes.WoodedHills : Biomes.FlowerForest
                5 => 19, // Biomes.Taiga => Biomes.TaigaHills,
                6 => 134, // Biomes.Swamp => Biomes.SwampHills,
                12 => 13, // Biomes.SnowyTundra => Biomes.SnowyMountains,
                21 => me.Variant < 50 ? 22 : 168, // Biomes.Jungle => Biomes.JungleHills,
                27 => 28, // Biomes.BirchForest => Biomes.BirchForestHills,
                29 => 1, // Biomes.DarkForest => Biomes.Plains,
                30 => 31, // Biomes.SnowyTaiga => Biomes.SnowyTaigaHills,
                32 => 33, // Biomes.GiantTreeTaiga => Biomes.GiantTreeTaigaHills,
                35 => 36, // Biomes.Savanna => Biomes.SavannaPlateau,
                38 => 37, // Biomes.WoodedBadlandsPlateau => Biomes.Badlands,
                39 => 37, // Biomes.BadlandsPlateau => Biomes.Badlands,
                160 => 161, // Biomes.GiantSpruceTaiga => Biomes.GiantSpruceTaigaHills
                _ => me.Biome
            };
            return me;
        }

        private VoronoiCell ProcessRiversAndBeaches(VoronoiCell me, VoronoiCell nearest)
        {
            var beachSize = 0.05;
            var riverSize = 0.03;

            var dist = nearest.DistanceToPoint - me.DistanceToPoint;

            if (dist > beachSize) { return me; }

            if (!Oceans.Contains(me.Biome) && !Oceans.Contains(nearest.Biome))
            {
                if (dist <= riverSize)
                {
                    if (me.BaseBiome == BaseBiome.Frozen || me.BaseBiome == BaseBiome.FrozenRare)
                        me.Biome = 11; // Biomes.FrozenRiver
                    else
                        me.Biome = 7; // Biomes.River
                }
                return me;
            }

            // If ocean bordering non-ocean
            if (Oceans.Contains(me.Biome) && !Oceans.Contains(nearest.Biome))
            {
                if (dist <= beachSize)
                {
                    me.Biome = me.Biome switch
                    {
                        10 or 50 => 26, // if frozen ocean, snowy beach
                        13 => 25, // if snowy mountains, stone shore
                        _ => 16 // Biomes.Beach
                    };
                }
            }
            return me;
        }

        private int ProcessBiomeRules(VoronoiCell me, VoronoiCell nearest)
        {
            double averageDistance = (me.DistanceToPoint + nearest.DistanceToPoint) / 2.0;

            // Process variants
            me.Biome = ProcessVariants(me, averageDistance);
            nearest.Biome = ProcessVariants(nearest, averageDistance);

            // Process neighbor rules
            me = ProcessNeighborRules(me, nearest, averageDistance);

            // Process biome center
            me = ProcessBiomeCenterRules(me, averageDistance);

            // Process edges
            me = ProcessRiversAndBeaches(me, nearest);

            return me.Biome;
        }

        private (BaseBiome, int) GetBaseBiome(double noise)
        {
            // Shift the whole map up by 1/2 for more land than sea.
            noise += 0.5;
            // Scale land (now 0 => 1.5) back down to 0 => 1
            // Scale sea (now -0.5 => 0) back to -1 => 0
            noise = noise > 0 ? noise * 0.667 : noise * 2.0;

            if (noise < 0)
            {
                noise *= 4.0; // 4 ocean types
                return ((BaseBiome)Math.Floor(noise), 0);
            }
            else
            {
                // Basically, the first 2 decimals becomes the varient.
                // So varient will be b/w 0 => 99
                int varient = (int)(noise * 1000.0) % 100;

                // 4 base overworld types but we want the ratio to be
                // 2 parts medium, 2 parts cold, 1 part frozen, 1 part dry
                noise *= 6.0;
                int val = (int)noise;
                return val switch
                {
                    // 18% chance for a rare medium biome.
                    0 or 1 => varient <= 18 ? (BaseBiome.MediumRare, varient) : (BaseBiome.Medium, varient),
                    // 15% chance for a rare cold biome.
                    2 or 3 => varient <= 15 ? (BaseBiome.ColdRare, varient) : (BaseBiome.Cold, varient),
                    // There are no frozen rare biomes.
                    4 => (BaseBiome.Frozen, varient),
                    // 10% chance for a rare dry biome.
                    5 => varient <= 10 ? (BaseBiome.DryRare, varient) : (BaseBiome.Dry, varient),
                    _ => (BaseBiome.Medium, varient),
                };
            }
        }

        internal struct VoronoiCell
        {
            public (int x, int z) Index { get; set; }
            public (double x, double z) Point { get; set; }
            public double DistanceToPoint { get; set; }
            public BaseBiome BaseBiome { get; set; }
            public int Variant { get; set; }
            public int Biome { get; set; } //TODO: When Biomes moves to API, fix this.
        }
    }
}
