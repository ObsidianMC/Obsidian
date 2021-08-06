using SharpNoise;
using SharpNoise.Modules;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.API.Noise
{
    public class VoronoiBiomes : Module
    {
        public int Seed { get; set; }

        public double Frequency { get; set; }

        public double BorderSize { get; set; } = 0.07;

        public enum BiomeNoiseValue : int
        {
            DeepOcean = -10,
            DeepFrozenOcean = -9,
            FrozenOcean = -8,
            DeepColdOcean = -7,
            ColdOcean = -6,
            DeepLukewarmOcean = -5,
            LukewarmOcean = -4,
            DeepWarmOcean = -3,
            WarmOcean = -2,
            Beach = -1,
            River = 0,
            Badlands,
            Desert,
            Savanna,
            ShatteredSavanna,
            SavannaPlateau,
            ShatteredSavannaPlateau,
            Jungle,
            Plains,
            Swamp,
            DarkForest,
            ExtremeHills,
            Forest,
            TallBirchForest,
            BirchForest,
            MountainMeadow,
            GiantTreeTaiga,
            GiantSpruceTaiga,
            Taiga,
            StoneShore,
            SnowyBeach,
            SnowyTundra,
            SnowyTaiga,
            MountainGrove,
            SnowySlopes,
            LoftyPeaks,
            SnowCappedPeaks,
            MushroomFields,
        }

        public VoronoiBiomes() : base(0)
        {

        }

        public override double GetValue(double x, double y, double z)
        {
            x *= Frequency;
            z *= Frequency;
            

            var xint = (x > 0D) ? (int)x : (int)x - 1;
            var zint = (z > 0D) ? (int)z : (int)z - 1;

            var cells = new List<VoronoiCell>();

            for (var zCur = zint - 2; zCur <= zint + 2; zCur++)
            {
                for (var xCur = xint - 2; xCur <= xint + 2; xCur++)
                {
                    var xPos = xCur + NoiseGenerator.ValueNoise3D(xCur, 0, zCur, Seed);
                    var zPos = zCur + NoiseGenerator.ValueNoise3D(xCur, 0, zCur, Seed + 2);
                    var xDist = xPos - x;
                    var zDist = zPos - z;
                    double dist = Math.Max(Math.Abs(xDist), Math.Abs(zDist));
                    var cell = new VoronoiCell
                    {
                        Index = (xint, zint),
                        DistanceToPoint = dist,
                        Point = (xPos, zPos)
                    };

                    cells.Add(cell);
                }
            }

            cells = cells.OrderBy(a => a.DistanceToPoint).ToList();
            var center = cells.First();
            var noiseVal = NoiseGenerator.ValueNoise3D((int)Math.Floor(center.Point.x), 0, (int)Math.Floor(center.Point.z));
            var returnVal = noiseVal > 0 ? Math.Floor(noiseVal * (int)BiomeNoiseValue.SnowyTaiga) : Math.Floor(noiseVal * 10);
            if (returnVal == (double)BiomeNoiseValue.River)
            {
                returnVal = (double)BiomeNoiseValue.WarmOcean;
            }
            else if (returnVal == (double)BiomeNoiseValue.Beach)
            {
                returnVal = (double)BiomeNoiseValue.ColdOcean;
            }

            if (returnVal > 0) // if land
            {
                var bs = BorderSize;
                cells.Remove(center);
                var nearest = cells.First();
                if (nearest.DistanceToPoint - center.DistanceToPoint <= bs)
                {
                    var nearestVal = NoiseGenerator.ValueNoise3D((int)Math.Floor(nearest.Point.x), 0, (int)Math.Floor(nearest.Point.z));
                    if (nearestVal < 0) // IF border with an ocean
                    {
                        returnVal = (Enum.GetName(typeof(BiomeNoiseValue), (int)returnVal) ?? "howdishapn").Contains("Snow") ?
                            (double)BiomeNoiseValue.SnowyBeach :
                            (double)BiomeNoiseValue.Beach;
                    }
                }
            }
            else // if ocean
            {

            }

            // Less than 0 will be ocean. Search for any land with ocean on all sides.
/*            voronois.TryGetValue((center.x + 1, center.z), out var east);
            voronois.TryGetValue((center.x - 1, center.z), out var west);
            voronois.TryGetValue((center.x, center.z + 1), out var north);
            voronois.TryGetValue((center.x, center.z - 1), out var south);

            if (east < 0 && west < 0 && north < 0 && south < 0)
            {
                returnVal = -1;
            }*/

            return returnVal;
        }

        public class VoronoiCell
        {
            public (int x, int z) Index { get; set; }

            public double DistanceToPoint { get; set; }

            public (double x, double z) Point { get; set; }
        }
    }
}
