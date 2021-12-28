using SharpNoise;
using SharpNoise.Modules;
using System.Runtime.CompilerServices;

namespace Obsidian.API.Noise;

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

    internal readonly List<Biomes> Oceans = new()
    {
        Biomes.DeepOcean,
        Biomes.DeepFrozenOcean,
        Biomes.DeepColdOcean,
        Biomes.DeepLukewarmOcean,
        Biomes.FrozenOcean,
        Biomes.ColdOcean,
        Biomes.LukewarmOcean,
        Biomes.WarmOcean,
        Biomes.Ocean,
    };

    public VoronoiBiomes() : base(0)
    {

    }

    [SkipLocalsInit]
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

                cells[index++] = new VoronoiCell
                {
                    Index = (xCur, zCur),
                    DistanceToPoint = dist,
                    Point = (xPos, zPos),
                    // Wasteful to calculate below for all cells right now.
                    // Calculate cells we care about later.
                    BaseBiome = 0.0,
                    Biome = 0
                };
            }
        }

        VoronoiCell me, nearest;
        Unsafe.SkipInit(out me);
        Unsafe.SkipInit(out nearest);
        GetMin(cells, ref me, ref nearest);

        var meVal = NoiseGenerator.ValueNoise3D((int)Math.Floor(me.Point.x), 0, (int)Math.Floor(me.Point.z));
        (me.BaseBiome, me.Variant) = GetBaseBiome(meVal);
        var nearestVal = NoiseGenerator.ValueNoise3D((int)Math.Floor(nearest.Point.x), 0, (int)Math.Floor(nearest.Point.z));
        (nearest.BaseBiome, nearest.Variant) = GetBaseBiome(nearestVal);

        return (double)ProcessBiomeRules(me, nearest);
    }

    private static void GetMin(ReadOnlySpan<VoronoiCell> cells, ref VoronoiCell min, ref VoronoiCell secondMin)
    {
        if (cells[1].DistanceToPoint > cells[0].DistanceToPoint)
        {
            min = cells[0];
            secondMin = cells[1];
        }
        else
        {
            min = cells[1];
            secondMin = cells[0];
        }

        for (int i = 2; i < cells.Length; i++)
        {
            if (cells[i].DistanceToPoint < min.DistanceToPoint)
            {
                secondMin = min;
                min = cells[i];
            }
            else if (cells[i].DistanceToPoint < secondMin.DistanceToPoint)
            {
                secondMin = cells[i];
            }
        }
    }

    private static Biomes ProcessVariants(VoronoiCell me, double averageDistance)
    {
        if ((int)me.BaseBiome < 0) // if ocean
        {
            // 65% of the ocean will be deep.
            if (me.DistanceToPoint < averageDistance * 0.65)
            {
                me.BaseBiome -= 4; // deep variant
            }

            // 5% chance that a warm ocean has a mooshroom island.
            if (me.BaseBiome == BaseBiome.DeepWarmOcean && me.Variant > 5 && me.Variant < 10)
            {
                if (me.DistanceToPoint < averageDistance * 0.15)
                {
                    return Biomes.MushroomFields;
                }
                else if (me.DistanceToPoint < averageDistance * 0.19)
                {
                    return Biomes.MushroomFields;
                }
            }

            // Map base biomes to proper Biomes values
            return me.BaseBiome switch
            {
                BaseBiome.DeepOcean => Biomes.DeepOcean,
                BaseBiome.DeepFrozenOcean => Biomes.DeepFrozenOcean,
                BaseBiome.DeepColdOcean => Biomes.DeepColdOcean,
                BaseBiome.DeepLukewarmOcean => Biomes.DeepLukewarmOcean,
                BaseBiome.DeepWarmOcean => Biomes.WarmOcean,
                BaseBiome.FrozenOcean => Biomes.FrozenOcean,
                BaseBiome.ColdOcean => Biomes.ColdOcean,
                BaseBiome.LukewarmOcean => Biomes.LukewarmOcean,
                BaseBiome.WarmOcean => Biomes.WarmOcean,
                _ => Biomes.Ocean,
            };
        }
        else // if land
        {
            switch (me.BaseBiome)
            {
                case BaseBiome.Dry:
                    {
                        if (me.Variant < 50)
                            return Biomes.Desert;
                        if (me.Variant < 83)
                            return Biomes.Savanna;
                        else
                            return Biomes.Plains;
                    }
                case BaseBiome.DryRare:
                    {
                        if (me.Variant < 33)
                            return Biomes.ErodedBadlands;
                        else
                            return Biomes.WoodedBadlands;
                    }
                case BaseBiome.Medium:
                    {
                        if (me.Variant < 20)
                            return Biomes.Forest;
                        if (me.Variant < 40)
                            return Biomes.DarkForest;
                        if (me.Variant < 60)
                            return Biomes.BirchForest;
                        if (me.Variant < 80)
                            return Biomes.JaggedPeaks;
                        if (me.Variant < 90)
                            return Biomes.Swamp;
                        else
                            return Biomes.Plains;
                    }
                case BaseBiome.MediumRare:
                    {
                        return Biomes.Jungle;
                    }
                case BaseBiome.Cold:
                    {
                        if (me.Variant < 25)
                            return Biomes.OldGrowthSpruceTaiga;
                        if (me.Variant < 50)
                            return Biomes.FrozenPeaks;
                        if (me.Variant < 75)
                            return Biomes.Taiga;
                        else
                            return Biomes.Plains;
                    }
                case BaseBiome.ColdRare:
                    {
                        return Biomes.SnowyTaiga;
                    }
                case BaseBiome.Frozen:
                    {
                        if (me.Variant < 75)
                            return Biomes.SnowyPlains;
                        else
                            return Biomes.SnowyTaiga;
                    }
                case BaseBiome.FrozenRare:
                    {
                        return Biomes.Plains; // There are no rare frozen biomes (yet?)
                    }
                default:
                    return Biomes.Plains;
            }
        }
    }

    private static VoronoiCell ProcessNeighborRules(VoronoiCell me, VoronoiCell neighbor, double averageDistance)
    {
        // Only run neighbor logic for outter 20% of a biome
        // Should save a lot of CPU cycles if we bail out now.
        if (me.DistanceToPoint < averageDistance * 0.80) { return me; }

        // Abandon hope, all ye who enter here
        switch (me.Biome)
        {
            // If a frozen or cold ocean borders a warm ocean,
            // then place a lukewarm ocean at the edge.
            case Biomes.FrozenOcean:
            case Biomes.DeepFrozenOcean:
            case Biomes.ColdOcean:
            case Biomes.DeepColdOcean:
                if (neighbor.Biome == Biomes.WarmOcean)
                    me.Biome = Biomes.LukewarmOcean;
                return me;

            // Badlands plateau and wooded badlands plateau generate
            // regular badlands on all edges.
            case Biomes.WoodedBadlands:
            case Biomes.ErodedBadlands:
                me.Biome = Biomes.Badlands;
                return me;

            // Giant tree taiga generates the regular taiga on all edges,
            // unless there is a pre-existing snowy Taiga or taiga bordering it.
            case Biomes.OldGrowthSpruceTaiga:
                if (neighbor.Biome != Biomes.SnowyTaiga)
                    me.Biome = Biomes.Taiga;
                return me;
            // If a desert borders a snowy tundra, a wooded mountain generates.
            case Biomes.Desert:
                if (neighbor.Biome == Biomes.SnowyPlains)
                    me.Biome = Biomes.FrozenPeaks;
                return me;
            // If a swamp borders a jungle, a jungle edge generate.
            // If a swamp borders a desert, snowy taiga, or snowy tundra,
            // a plains biome generates.
            case Biomes.Swamp:
                if (neighbor.Biome == Biomes.Jungle)
                    me.Biome = Biomes.SparseJungle;
                else if (neighbor.Biome == Biomes.Desert ||
                    neighbor.Biome == Biomes.SnowyTaiga)
                    me.Biome = Biomes.Plains;
                return me;
            default:
                return me;
        }
    }

    private static VoronoiCell ProcessBiomeCenterRules(VoronoiCell me, double averageDistance)
    {
        if (me.DistanceToPoint > averageDistance * 0.40) { return me; }

        me.Biome = me.Biome switch
        {
            Biomes.Plains => me.Variant < 30 ? Biomes.SunflowerPlains : me.Variant < 60 ? Biomes.WindsweptHills : Biomes.Forest,
            Biomes.Desert => Biomes.WindsweptGravellyHills,
            Biomes.JaggedPeaks => me.BaseBiome == BaseBiome.Cold ? Biomes.FrozenPeaks : Biomes.StonyPeaks,
            Biomes.Forest => me.BaseBiome == BaseBiome.Cold ? Biomes.WindsweptHills : Biomes.FlowerForest,
            Biomes.Taiga => Biomes.WindsweptHills,
            Biomes.Swamp => Biomes.WindsweptHills,
            Biomes.SnowyTaiga => Biomes.SnowySlopes,
            Biomes.Jungle => me.Variant < 50 ? Biomes.SparseJungle : Biomes.BambooJungle,
            Biomes.BirchForest => Biomes.WindsweptHills,
            Biomes.DarkForest => Biomes.Plains,
            Biomes.OldGrowthPineTaiga => Biomes.WindsweptHills,
            Biomes.Savanna => Biomes.SavannaPlateau,
            Biomes.WoodedBadlands => Biomes.Badlands,
            Biomes.OldGrowthSpruceTaiga => Biomes.WindsweptHills,
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
                me.Biome = me.BaseBiome == BaseBiome.Frozen ||
                    me.BaseBiome == BaseBiome.FrozenRare ||
                    nearest.BaseBiome == BaseBiome.Frozen ||
                    nearest.BaseBiome == BaseBiome.FrozenRare ? Biomes.FrozenRiver : Biomes.River;

            return me;
        }

        // If ocean bordering non-ocean
        if (Oceans.Contains(me.Biome) && !Oceans.Contains(nearest.Biome))
        {
            if (dist <= beachSize)
            {
                me.Biome = me.Biome switch
                {
                    Biomes.FrozenOcean or Biomes.DeepFrozenOcean => Biomes.SnowyBeach,
                    Biomes.SnowySlopes => Biomes.StonyShore,
                    _ => Biomes.Beach
                };
            }
        }
        return me;
    }

    private Biomes ProcessBiomeRules(VoronoiCell me, VoronoiCell nearest)
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

    private static (BaseBiome, int) GetBaseBiome(double noise)
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
            // Basically, the first 2 decimals becomes the variant.
            // So variant will be b/w 0 => 99
            int variant = (int)(noise * 1000.0) % 100;

            // 4 base overworld types but we want the ratio to be
            // 2 parts medium, 2 parts cold, 1 part frozen, 1 part dry
            noise *= 6.0;
            int val = (int)noise;
            return val switch
            {
                // 18% chance for a rare medium biome.
                0 or 1 => variant <= 18 ? (BaseBiome.MediumRare, variant) : (BaseBiome.Medium, variant),
                // 15% chance for a rare cold biome.
                2 or 3 => variant <= 15 ? (BaseBiome.ColdRare, variant) : (BaseBiome.Cold, variant),
                // There are no frozen rare biomes.
                4 => (BaseBiome.Frozen, variant),
                // 10% chance for a rare dry biome.
                5 => variant <= 10 ? (BaseBiome.DryRare, variant) : (BaseBiome.Dry, variant),
                _ => (BaseBiome.Medium, variant),
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
        public Biomes Biome { get; set; }
    }
}
