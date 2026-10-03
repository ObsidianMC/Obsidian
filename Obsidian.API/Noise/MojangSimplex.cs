using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.Noise;

/// <summary>
/// Vanilla simplex noise (<c>SimplexNoise</c>), used by <see cref="PerlinSimplexNoise"/> and the End islands.
/// </summary>
public sealed class SimplexNoise
{
    /// <summary>Gradient vectors as flattened (x, y, z) triples; the last four repeat earlier ones.</summary>
    private static ReadOnlySpan<sbyte> Gradients =>
    [
        1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1, 0,
        1, 0, 1, -1, 0, 1, 1, 0, -1, -1, 0, -1,
        0, 1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1,
        1, 1, 0, 0, -1, 1, -1, 1, 0, 0, -1, -1,
    ];

    private const double F3 = 0.3333333333333333;
    private const double G3 = 0.16666666666666666;

    private static readonly double Sqrt3 = Math.Sqrt(3.0);
    private static readonly double F2 = 0.5 * (Sqrt3 - 1.0);
    private static readonly double G2 = (3.0 - Sqrt3) / 6.0;

    private readonly byte[] p = new byte[256];

    public double Xo { get; }

    public double Yo { get; }

    public double Zo { get; }

    /// <summary>Creates a noise, consuming 3 doubles and 256 bounded ints from <paramref name="random"/>.</summary>
    public SimplexNoise(IRandomSource random)
    {
        this.Xo = random.NextDouble() * 256.0;
        this.Yo = random.NextDouble() * 256.0;
        this.Zo = random.NextDouble() * 256.0;

        for (var i = 0; i < 256; i++)
            this.p[i] = (byte)i;

        for (var i = 0; i < 256; i++)
        {
            var j = i + random.NextInt(256 - i);
            (this.p[i], this.p[j]) = (this.p[j], this.p[i]);
        }
    }

    // The gradient components as doubles, one table per axis, which saves converting them per sample.
    private static ReadOnlySpan<double> GradientsX => [1, -1, 1, -1, 1, -1, 1, -1, 0, 0, 0, 0, 1, 0, -1, 0];

    private static ReadOnlySpan<double> GradientsY => [1, 1, -1, -1, 0, 0, 0, 0, 1, -1, 1, -1, 1, -1, 1, -1];

    private static ReadOnlySpan<double> GradientsZ => [0, 0, 0, 0, 1, 1, -1, -1, 1, 1, -1, -1, 0, 1, 0, -1];

    /// <summary>Dot product of gradient <paramref name="gradient"/> (0-15) with (x, y, z).</summary>
    internal static double Dot(int gradient, double x, double y, double z)
    {
        gradient &= 15;
        return GradientsX[gradient] * x + GradientsY[gradient] * y + GradientsZ[gradient] * z;
    }

    /// <summary>Component <paramref name="axis"/> (0 = x, 1 = y, 2 = z) of gradient <paramref name="gradient"/>.</summary>
    internal static int GradientComponent(int gradient, int axis) => Gradients[gradient * 3 + axis];

    /// <summary>2D simplex noise in roughly <c>[-1, 1]</c>.</summary>
    public double GetValue(double x, double y)
    {
        var skew = (x + y) * F2;
        var i = Mth.Floor(x + skew);
        var j = Mth.Floor(y + skew);
        var unskew = (i + j) * G2;
        var originX = i - unskew;
        var originY = j - unskew;
        var x0 = x - originX;
        var y0 = y - originY;

        int i1, j1;
        if (x0 > y0)
        {
            i1 = 1;
            j1 = 0;
        }
        else
        {
            i1 = 0;
            j1 = 1;
        }

        var x1 = x0 - i1 + G2;
        var y1 = y0 - j1 + G2;
        var x2 = x0 - 1.0 + 2.0 * G2;
        var y2 = y0 - 1.0 + 2.0 * G2;

        var ii = i & 0xFF;
        var jj = j & 0xFF;
        var gi0 = this.P(ii + this.P(jj)) % 12;
        var gi1 = this.P(ii + i1 + this.P(jj + j1)) % 12;
        var gi2 = this.P(ii + 1 + this.P(jj + 1)) % 12;

        var n0 = CornerNoise(gi0, x0, y0, 0.0, 0.5);
        var n1 = CornerNoise(gi1, x1, y1, 0.0, 0.5);
        var n2 = CornerNoise(gi2, x2, y2, 0.0, 0.5);
        return 70.0 * (n0 + n1 + n2);
    }

    /// <summary>3D simplex noise in roughly <c>[-1, 1]</c>.</summary>
    public double GetValue(double x, double y, double z)
    {
        var skew = (x + y + z) * F3;
        var i = Mth.Floor(x + skew);
        var j = Mth.Floor(y + skew);
        var k = Mth.Floor(z + skew);
        var unskew = (i + j + k) * G3;
        var originX = i - unskew;
        var originY = j - unskew;
        var originZ = k - unskew;
        var x0 = x - originX;
        var y0 = y - originY;
        var z0 = z - originZ;

        int i1, j1, k1, i2, j2, k2;
        if (x0 >= y0)
        {
            if (y0 >= z0)
            {
                (i1, j1, k1, i2, j2, k2) = (1, 0, 0, 1, 1, 0);
            }
            else if (x0 >= z0)
            {
                (i1, j1, k1, i2, j2, k2) = (1, 0, 0, 1, 0, 1);
            }
            else
            {
                (i1, j1, k1, i2, j2, k2) = (0, 0, 1, 1, 0, 1);
            }
        }
        else if (y0 < z0)
        {
            (i1, j1, k1, i2, j2, k2) = (0, 0, 1, 0, 1, 1);
        }
        else if (x0 < z0)
        {
            (i1, j1, k1, i2, j2, k2) = (0, 1, 0, 0, 1, 1);
        }
        else
        {
            (i1, j1, k1, i2, j2, k2) = (0, 1, 0, 1, 1, 0);
        }

        var x1 = x0 - i1 + G3;
        var y1 = y0 - j1 + G3;
        var z1 = z0 - k1 + G3;
        var x2 = x0 - i2 + F3;
        var y2 = y0 - j2 + F3;
        var z2 = z0 - k2 + F3;
        var x3 = x0 - 1.0 + 0.5;
        var y3 = y0 - 1.0 + 0.5;
        var z3 = z0 - 1.0 + 0.5;

        var ii = i & 0xFF;
        var jj = j & 0xFF;
        var kk = k & 0xFF;
        var gi0 = this.P(ii + this.P(jj + this.P(kk))) % 12;
        var gi1 = this.P(ii + i1 + this.P(jj + j1 + this.P(kk + k1))) % 12;
        var gi2 = this.P(ii + i2 + this.P(jj + j2 + this.P(kk + k2))) % 12;
        var gi3 = this.P(ii + 1 + this.P(jj + 1 + this.P(kk + 1))) % 12;

        var n0 = CornerNoise(gi0, x0, y0, z0, 0.6);
        var n1 = CornerNoise(gi1, x1, y1, z1, 0.6);
        var n2 = CornerNoise(gi2, x2, y2, z2, 0.6);
        var n3 = CornerNoise(gi3, x3, y3, z3, 0.6);
        return 32.0 * (n0 + n1 + n2 + n3);
    }

    private int P(int index) => this.p[index & 0xFF];

    private static double CornerNoise(int gradient, double x, double y, double z, double falloff)
    {
        var t = falloff - x * x - y * y - z * z;
        if (t < 0.0)
            return 0.0;

        t *= t;
        return t * t * Dot(gradient, x, y, z);
    }
}
