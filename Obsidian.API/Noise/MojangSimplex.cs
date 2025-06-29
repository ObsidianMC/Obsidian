using System;
namespace Obsidian.API.Noise;


public class SimplexNoise
{
    internal static readonly int[][] GRADIENT = new int[][]
    {
            new[] { 1, 1, 0 }, new[] { -1, 1, 0 }, new[] { 1, -1, 0 }, new[] { -1, -1, 0 },
            new[] { 1, 0, 1 }, new[] { -1, 0, 1 }, new[] { 1, 0, -1 }, new[] { -1, 0, -1 },
            new[] { 0, 1, 1 }, new[] { 0, -1, 1 }, new[] { 0, 1, -1 }, new[] { 0, -1, -1 },
            new[] { 1, 1, 0 }, new[] { 0, -1, 1 }, new[] { -1, 1, 0 }, new[] { 0, -1, -1 }
    };

    private static readonly double Sqrt3 = Math.Sqrt(3.0);
    private static readonly double F2 = 0.5 * (Sqrt3 - 1.0);
    private static readonly double G2 = (3.0 - Sqrt3) / 6.0;

    private readonly int[] permutation = new int[512];
    public readonly double OffsetX;
    public readonly double OffsetY;
    public readonly double OffsetZ;

    public SimplexNoise(Random random)
    {
        OffsetX = random.NextDouble() * 256.0;
        OffsetY = random.NextDouble() * 256.0;
        OffsetZ = random.NextDouble() * 256.0;

        int[] tempPermutation = new int[256];
        for (int i = 0; i < 256; i++) tempPermutation[i] = i;

        for (int i = 0; i < 256; i++)
        {
            int j = random.Next(256 - i) + i;
            (tempPermutation[i], tempPermutation[j]) = (tempPermutation[j], tempPermutation[i]);
        }

        for (int i = 0; i < 512; i++)
        {
            permutation[i] = tempPermutation[i % 256];
        }
    }

    private int Permutate(int value) => permutation[value & 255];

    public static double Dot(int[] gradient, double x, double y, double z) =>
        gradient[0] * x + gradient[1] * y + gradient[2] * z;

    private double CalculateCornerNoise(int gradientIndex, double x, double y, double z, double factor)
    {
        double squaredSum = factor - x * x - y * y - z * z;
        if (squaredSum < 0.0) return 0.0;

        squaredSum *= squaredSum;
        return squaredSum * squaredSum * Dot(GRADIENT[gradientIndex], x, y, z);
    }

    public double GetValue(double x, double y)
    {
        double skew = (x + y) * F2;
        int skewedX = (int)Math.Floor(x + skew);
        int skewedY = (int)Math.Floor(y + skew);

        double unskew = (skewedX + skewedY) * G2;
        double unskewedX = skewedX - unskew;
        double unskewedY = skewedY - unskew;

        double deltaX = x - unskewedX;
        double deltaY = y - unskewedY;

        int offsetX, offsetY;
        if (deltaX > deltaY)
        {
            offsetX = 1; offsetY = 0;
        }
        else
        {
            offsetX = 0; offsetY = 1;
        }

        double x1 = deltaX - offsetX + G2;
        double y1 = deltaY - offsetY + G2;

        double x2 = deltaX - 1.0 + 2.0 * G2;
        double y2 = deltaY - 1.0 + 2.0 * G2;

        int gradientIndex0 = Permutate(skewedX + Permutate(skewedY)) % 12;
        int gradientIndex1 = Permutate(skewedX + offsetX + Permutate(skewedY + offsetY)) % 12;
        int gradientIndex2 = Permutate(skewedX + 1 + Permutate(skewedY + 1)) % 12;

        double noise0 = CalculateCornerNoise(gradientIndex0, deltaX, deltaY, 0.0, 0.5);
        double noise1 = CalculateCornerNoise(gradientIndex1, x1, y1, 0.0, 0.5);
        double noise2 = CalculateCornerNoise(gradientIndex2, x2, y2, 0.0, 0.5);

        return 70.0 * (noise0 + noise1 + noise2);
    }

    public double GetValue(double x, double y, double z)
    {
        const double SkewFactor = 1.0 / 3.0;
        double skew = (x + y + z) * SkewFactor;
        int skewedX = (int)Math.Floor(x + skew);
        int skewedY = (int)Math.Floor(y + skew);
        int skewedZ = (int)Math.Floor(z + skew);

        const double UnskewFactor = 1.0 / 6.0;
        double unskew = (skewedX + skewedY + skewedZ) * UnskewFactor;
        double unskewedX = skewedX - unskew;
        double unskewedY = skewedY - unskew;
        double unskewedZ = skewedZ - unskew;

        double deltaX = x - unskewedX;
        double deltaY = y - unskewedY;
        double deltaZ = z - unskewedZ;

        int x1, y1, z1, x2, y2, z2;

        if (deltaX >= deltaY)
        {
            if (deltaY >= deltaZ)
            {
                x1 = 1; y1 = 0; z1 = 0;
                x2 = 1; y2 = 1; z2 = 0;
            }
            else if (deltaX >= deltaZ)
            {
                x1 = 1; y1 = 0; z1 = 0;
                x2 = 1; y2 = 0; z2 = 1;
            }
            else
            {
                x1 = 0; y1 = 0; z1 = 1;
                x2 = 1; y2 = 0; z2 = 1;
            }
        }
        else
        {
            if (deltaY < deltaZ)
            {
                x1 = 0; y1 = 0; z1 = 1;
                x2 = 0; y2 = 1; z2 = 1;
            }
            else if (deltaX < deltaZ)
            {
                x1 = 0; y1 = 1; z1 = 0;
                x2 = 0; y2 = 1; z2 = 1;
            }
            else
            {
                x1 = 0; y1 = 1; z1 = 0;
                x2 = 1; y2 = 1; z2 = 0;
            }
        }

        double x1Pos = deltaX - x1 + UnskewFactor;
        double y1Pos = deltaY - y1 + UnskewFactor;
        double z1Pos = deltaZ - z1 + UnskewFactor;

        double x2Pos = deltaX - x2 + 2.0 * UnskewFactor;
        double y2Pos = deltaY - y2 + 2.0 * UnskewFactor;
        double z2Pos = deltaZ - z2 + 2.0 * UnskewFactor;

        double x3Pos = deltaX - 1.0 + 3.0 * UnskewFactor;
        double y3Pos = deltaY - 1.0 + 3.0 * UnskewFactor;
        double z3Pos = deltaZ - 1.0 + 3.0 * UnskewFactor;

        int gradientIndex0 = Permutate(skewedX + Permutate(skewedY + Permutate(skewedZ))) % 12;
        int gradientIndex1 = Permutate(skewedX + x1 + Permutate(skewedY + y1 + Permutate(skewedZ + z1))) % 12;
        int gradientIndex2 = Permutate(skewedX + x2 + Permutate(skewedY + y2 + Permutate(skewedZ + z2))) % 12;
        int gradientIndex3 = Permutate(skewedX + 1 + Permutate(skewedY + 1 + Permutate(skewedZ + 1))) % 12;

        double noise0 = CalculateCornerNoise(gradientIndex0, deltaX, deltaY, deltaZ, 0.6);
        double noise1 = CalculateCornerNoise(gradientIndex1, x1Pos, y1Pos, z1Pos, 0.6);
        double noise2 = CalculateCornerNoise(gradientIndex2, x2Pos, y2Pos, z2Pos, 0.6);
        double noise3 = CalculateCornerNoise(gradientIndex3, x3Pos, y3Pos, z3Pos, 0.6);

        return 32.0 * (noise0 + noise1 + noise2 + noise3);
    }
}
