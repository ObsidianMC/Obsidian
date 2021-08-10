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

        public double RiverSize { get; set; } = 0.07;

        public double BeachSize { get; set; } = 0.15;

        public enum BiomeNoiseValue : int
        {
            DeepOcean = -9,
            DeepFrozenOcean = -8,
            DeepColdOcean = -7,
            DeepLukewarmOcean = -6,
            DeepWarmOcean = -5,
            FrozenOcean = -4,
            ColdOcean = -3,
            LukewarmOcean = -2,
            WarmOcean = -1,
            Plains = 0, // Start common
            Forest = 1,
            Desert = 2,
            Jungle = 3,
            BirchForest = 4,
            Swamp = 5,
            Savanna = 6,
            DarkForest = 7,
            Badlands = 8,
            Taiga = 9, 
            SnowyTundra = 10, // End common
            SavannaPlateau,
            ShatteredSavannaPlateau,
            ShatteredSavanna,
            ExtremeHills,
            MountainMeadow,
            GiantTreeTaiga,
            GiantSpruceTaiga,
            TallBirchForest,
            StoneShore,
            SnowyBeach,
            SnowyTaiga,
            Beach,
            River,
            MountainGrove,
            SnowySlopes,
            LoftyPeaks,
            SnowCappedPeaks,
            MushroomFields,
            FrozenRiver
        }

        public readonly List<BiomeNoiseValue> BiomesWithRivers = new()
        {
            BiomeNoiseValue.Badlands,
            BiomeNoiseValue.Desert,
            BiomeNoiseValue.Forest,
            BiomeNoiseValue.GiantSpruceTaiga,
            BiomeNoiseValue.GiantTreeTaiga,
            BiomeNoiseValue.Jungle,
            BiomeNoiseValue.Plains,
            BiomeNoiseValue.Savanna,
            BiomeNoiseValue.SavannaPlateau,
            BiomeNoiseValue.TallBirchForest,
            BiomeNoiseValue.Taiga
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
            cells.Remove(center);

            var noiseVal = NoiseGenerator.ValueNoise3D((int)Math.Floor(center.Point.x), 0, (int)Math.Floor(center.Point.z)) + 0.2;

            // Scale the output that's normally -1 => 1 to -10 => 10 for common biomes
            var returnVal = ScaleNoise(noiseVal);
            
            var neighbor1 = ScaleNoise(NoiseGenerator.ValueNoise3D((int)Math.Floor(cells[0].Point.x), 0, (int)Math.Floor(cells[0].Point.z)));
            var neighbor2 = ScaleNoise(NoiseGenerator.ValueNoise3D((int)Math.Floor(cells[1].Point.x), 0, (int)Math.Floor(cells[1].Point.z)));
            var neighbor3 = ScaleNoise(NoiseGenerator.ValueNoise3D((int)Math.Floor(cells[2].Point.x), 0, (int)Math.Floor(cells[2].Point.z)));
            var neighbor4 = ScaleNoise(NoiseGenerator.ValueNoise3D((int)Math.Floor(cells[3].Point.x), 0, (int)Math.Floor(cells[3].Point.z)));
            
            // Tweak random outputs to be more worldlike
            if (returnVal <= 0) // if ocean
            {
                if (neighbor1 == returnVal) // If nearest neighbors are land, convert this to land
                {
                    returnVal = (BiomeNoiseValue)((int)returnVal - 4); // deep varient

                }
            }

            if (returnVal > 0) // if land
            {
                var nearest = cells.First();
                // IF border with an ocean
                if (neighbor1 < 0 && nearest.DistanceToPoint - center.DistanceToPoint <= RiverSize)
                {
                    returnVal = (Enum.GetName(typeof(BiomeNoiseValue), returnVal) ?? "howdishapn").Contains("Snow") ?
                        BiomeNoiseValue.SnowyBeach :
                        BiomeNoiseValue.Beach;
                }
                if (BiomesWithRivers.Contains(returnVal) && nearest.DistanceToPoint - center.DistanceToPoint <= BeachSize)
                {
                    returnVal = (Enum.GetName(typeof(BiomeNoiseValue), (int)returnVal) ?? "lolwut").Contains("Snow") ?
                            BiomeNoiseValue.FrozenRiver :
                            BiomeNoiseValue.River;
                }
            }
            return (double)returnVal;
        }

        private BiomeNoiseValue ScaleNoise(double noise)
        {
            return (BiomeNoiseValue)Math.Floor(noise > 0 ? noise * 10.0 : noise * 4.0);
        }

        public class VoronoiCell
        {
            public (int x, int z) Index { get; set; }

            public double DistanceToPoint { get; set; }

            public (double x, double z) Point { get; set; }
        }
    }
}
