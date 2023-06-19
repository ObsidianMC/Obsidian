using SharpNoise;
using SharpNoise.Modules;
using System.Runtime.CompilerServices;

namespace Obsidian.API.Noise;

public class VoronoiTunnels : Module
{
    public int Seed { get; set; }

    public double Frequency { get; set; }

    public VoronoiTunnels() : base(0)
    {

    }

    [SkipLocalsInit]
    public override double GetValue(double x, double y, double z)
    {
        x *= Frequency;
        y *= Frequency * 1.333;
        z *= Frequency;

        var xint = (x > 0D) ? (int)x : (int)x - 1;
        var yint = (y > 0D) ? (int)y : (int)y - 1;
        var zint = (z > 0D) ? (int)z : (int)z - 1;

        Span<VoronoiCell> cells = stackalloc VoronoiCell[27];

        int index = 0;
        for (var zCur = zint - 1; zCur <= zint + 1; zCur++)
        {
            for (var yCur = yint - 1; yCur <= yint + 1; yCur++)
            {
                for (var xCur = xint - 1; xCur <= xint + 1; xCur++)
                {
                    float xPos = (float)(xCur + NoiseGenerator.ValueNoise3D(xCur, yCur, zCur, Seed));
                    float yPos = (float)(yCur + NoiseGenerator.ValueNoise3D(xCur, yCur, zCur, Seed + 1));
                    float zPos = (float)(zCur + NoiseGenerator.ValueNoise3D(xCur, yCur, zCur, Seed + 2));
                    var xDist = xPos - x;
                    var yDist = yPos - y;
                    var zDist = zPos - z;
                    double dist = Math.Sqrt(xDist * xDist + yDist * yDist + zDist * zDist);

                    //double dist = Math.Abs(xDist) + Math.Abs(yDist) + Math.Abs(zDist);

                    cells[index++] = new VoronoiCell
                    {
                        Index = (xCur, zCur),
                        DistanceToPoint = dist,
                        Point = new VectorF(xPos, yPos, zPos),
                    };
                }
            }
        }

        Unsafe.SkipInit(out VoronoiCell primary);
        Unsafe.SkipInit(out VoronoiCell secondary);
        Unsafe.SkipInit(out VoronoiCell tertiary);
        GetMin(cells, ref primary, ref secondary, ref tertiary);

        if (primary.DistanceToPoint < 0.123)
            return 1.0;

        var distA = secondary.Point - primary.Point;
        var lenA = Math.Sqrt(distA.X * distA.X + distA.Y * distA.Y + distA.Z * distA.Z);
        if (primary.DistanceToPoint + secondary.DistanceToPoint <= lenA * (1.0 + Frequency))
            return 1.0;

        var distB = tertiary.Point - primary.Point;
        var lenB = Math.Sqrt(distB.X * distB.X + distB.Y * distB.Y + distB.Z * distB.Z);
        if (primary.DistanceToPoint + tertiary.DistanceToPoint <= lenB * (1.0 + Frequency))
            return 1.0;

        return 0;
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

    private static void GetMin(ReadOnlySpan<VoronoiCell> cells, ref VoronoiCell min, ref VoronoiCell secondMin, ref VoronoiCell thirdMin)
    {
        if (cells[1].DistanceToPoint > cells[0].DistanceToPoint)
        {
            min = cells[0];
            secondMin = cells[1];
            thirdMin = cells[2];
        }
        else
        {
            min = cells[2];
            secondMin = cells[1];
            thirdMin = cells[0];
        }

        for (int i = 2; i < cells.Length; i++)
        {
            if (cells[i].DistanceToPoint < min.DistanceToPoint)
            {
                thirdMin = secondMin;
                secondMin = min;
                min = cells[i];
            }
            else if (cells[i].DistanceToPoint < secondMin.DistanceToPoint)
            {
                thirdMin = secondMin;
                secondMin = cells[i];
            }
        }
    }

    internal struct VoronoiCell
    {
        public (int x, int z) Index { get; set; }
        public VectorF Point { get; set; }
        public double DistanceToPoint { get; set; }
    }
}
