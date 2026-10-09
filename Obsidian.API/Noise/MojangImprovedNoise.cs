using Obsidian.API.World.Generator.RandomSources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Obsidian.API.Noise;

/// <summary>
/// A single octave of vanilla Perlin noise (<c>ImprovedNoise</c>). Usually used through <see cref="PerlinNoise"/>.
/// </summary>
public sealed class ImprovedNoise
{
    /// <summary>Vanilla declares this as a float; the widened value (1.0000000116860974E-7) is what it uses.</summary>
    private const double ShiftUpEpsilon = 1.0E-7f;

    private readonly byte[] p = new byte[256];

    public double Xo { get; }

    public double Yo { get; }

    public double Zo { get; }

    /// <summary>Creates a noise, consuming 3 doubles and 256 bounded ints from <paramref name="random"/>.</summary>
    public ImprovedNoise(IRandomSource random)
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

    /// <summary>Samples the noise at (x, y, z). Result is roughly in <c>[-1, 1]</c>.</summary>
    public double Noise(double x, double y, double z) => this.Noise(x, y, z, 0.0, 0.0);

    /// <summary>
    /// Legacy sampling with vertical "smearing": the y offset inside the cell is quantized to multiples of
    /// <paramref name="yScale"/> (bounded by <paramref name="yMax"/>) when computing gradients.
    /// Used by <see cref="BlendedNoise"/> and <see cref="PerlinNoise.GetValue(double, double, double, double, double, bool)"/>.
    /// </summary>
    public double Noise(double x, double y, double z, double yScale, double yMax)
    {
        var shiftedX = x + this.Xo;
        var shiftedY = y + this.Yo;
        var shiftedZ = z + this.Zo;
        var floorX = FloorSaturated(shiftedX);
        var floorY = FloorSaturated(shiftedY);
        var floorZ = FloorSaturated(shiftedZ);
        var localX = shiftedX - floorX;
        var localY = shiftedY - floorY;
        var localZ = shiftedZ - floorZ;

        var yShift = 0.0;
        if (yScale != 0.0)
        {
            var clampedY = yMax >= 0.0 && yMax < localY ? yMax : localY;
            yShift = FloorSaturated(clampedY / yScale + ShiftUpEpsilon) * yScale;
        }

        // The floors are ints already, so the conversions don't need to saturate.
        return this.SampleAndLerp(double.ConvertToIntegerNative<int>(floorX), double.ConvertToIntegerNative<int>(floorY),
            double.ConvertToIntegerNative<int>(floorZ), localX, localY - yShift, localZ, localY);
    }

    /// <summary>
    /// <see cref="Mth.Floor"/> as a double. Wrapped coordinates are far inside the int range, where flooring in floating
    /// point gives the same value in fewer instructions; Mth.Floor still handles the rest, which it saturates.
    /// </summary>
    private static double FloorSaturated(double value)
    {
        var floor = Math.Floor(value);
        return Math.Abs(floor) <= int.MaxValue ? floor : Mth.Floor(value);
    }

    /// <summary>
    /// Samples the noise at (x, y, z) and adds its partial derivatives to <paramref name="derivatives"/> (length 3).
    /// </summary>
    public double NoiseWithDerivative(double x, double y, double z, Span<double> derivatives)
    {
        var shiftedX = x + this.Xo;
        var shiftedY = y + this.Yo;
        var shiftedZ = z + this.Zo;
        var cellX = Mth.Floor(shiftedX);
        var cellY = Mth.Floor(shiftedY);
        var cellZ = Mth.Floor(shiftedZ);

        return this.SampleWithDerivative(cellX, cellY, cellZ, shiftedX - cellX, shiftedY - cellY, shiftedZ - cellZ, derivatives);
    }

    private int P(int index) => this.p[index & 0xFF];

    // The mask keeps the index inside the 256 entries, so the bounds check can go.
    private static int P(ref byte p, int index) => Unsafe.Add(ref p, index & 0xFF);

    private static double GradDot(int hash, double x, double y, double z) => SimplexNoise.Dot(hash & 15, x, y, z);

    private double SampleAndLerp(int cellX, int cellY, int cellZ, double x, double y, double z, double yForSmoothing)
    {
        ref var p = ref MemoryMarshal.GetArrayDataReference(this.p);
        var a = P(ref p, cellX);
        var b = P(ref p, cellX + 1);
        var aa = P(ref p, a + cellY);
        var ab = P(ref p, a + cellY + 1);
        var ba = P(ref p, b + cellY);
        var bb = P(ref p, b + cellY + 1);

        var d000 = GradDot(P(ref p, aa + cellZ), x, y, z);
        var d100 = GradDot(P(ref p, ba + cellZ), x - 1.0, y, z);
        var d010 = GradDot(P(ref p, ab + cellZ), x, y - 1.0, z);
        var d110 = GradDot(P(ref p, bb + cellZ), x - 1.0, y - 1.0, z);
        var d001 = GradDot(P(ref p, aa + cellZ + 1), x, y, z - 1.0);
        var d101 = GradDot(P(ref p, ba + cellZ + 1), x - 1.0, y, z - 1.0);
        var d011 = GradDot(P(ref p, ab + cellZ + 1), x, y - 1.0, z - 1.0);
        var d111 = GradDot(P(ref p, bb + cellZ + 1), x - 1.0, y - 1.0, z - 1.0);

        // The y weight uses the unshifted local y, which is what makes the smeared sampling work.
        var tx = Mth.Smoothstep(x);
        var ty = Mth.Smoothstep(yForSmoothing);
        var tz = Mth.Smoothstep(z);
        return Mth.Lerp3(tx, ty, tz, d000, d100, d010, d110, d001, d101, d011, d111);
    }

    private double SampleWithDerivative(int cellX, int cellY, int cellZ, double x, double y, double z, Span<double> derivatives)
    {
        var a = this.P(cellX);
        var b = this.P(cellX + 1);
        var aa = this.P(a + cellY);
        var ab = this.P(a + cellY + 1);
        var ba = this.P(b + cellY);
        var bb = this.P(b + cellY + 1);

        var g000 = this.P(aa + cellZ) & 15;
        var g100 = this.P(ba + cellZ) & 15;
        var g010 = this.P(ab + cellZ) & 15;
        var g110 = this.P(bb + cellZ) & 15;
        var g001 = this.P(aa + cellZ + 1) & 15;
        var g101 = this.P(ba + cellZ + 1) & 15;
        var g011 = this.P(ab + cellZ + 1) & 15;
        var g111 = this.P(bb + cellZ + 1) & 15;

        var d000 = SimplexNoise.Dot(g000, x, y, z);
        var d100 = SimplexNoise.Dot(g100, x - 1.0, y, z);
        var d010 = SimplexNoise.Dot(g010, x, y - 1.0, z);
        var d110 = SimplexNoise.Dot(g110, x - 1.0, y - 1.0, z);
        var d001 = SimplexNoise.Dot(g001, x, y, z - 1.0);
        var d101 = SimplexNoise.Dot(g101, x - 1.0, y, z - 1.0);
        var d011 = SimplexNoise.Dot(g011, x, y - 1.0, z - 1.0);
        var d111 = SimplexNoise.Dot(g111, x - 1.0, y - 1.0, z - 1.0);

        var tx = Mth.Smoothstep(x);
        var ty = Mth.Smoothstep(y);
        var tz = Mth.Smoothstep(z);

        double LerpGradients(int axis) => Mth.Lerp3(tx, ty, tz,
            SimplexNoise.GradientComponent(g000, axis), SimplexNoise.GradientComponent(g100, axis),
            SimplexNoise.GradientComponent(g010, axis), SimplexNoise.GradientComponent(g110, axis),
            SimplexNoise.GradientComponent(g001, axis), SimplexNoise.GradientComponent(g101, axis),
            SimplexNoise.GradientComponent(g011, axis), SimplexNoise.GradientComponent(g111, axis));

        var gradientX = LerpGradients(0);
        var gradientY = LerpGradients(1);
        var gradientZ = LerpGradients(2);
        var slopeX = Mth.Lerp2(ty, tz, d100 - d000, d110 - d010, d101 - d001, d111 - d011);
        var slopeY = Mth.Lerp2(tz, tx, d010 - d000, d011 - d001, d110 - d100, d111 - d101);
        var slopeZ = Mth.Lerp2(tx, ty, d001 - d000, d101 - d100, d011 - d010, d111 - d110);

        derivatives[0] += gradientX + Mth.SmoothstepDerivative(x) * slopeX;
        derivatives[1] += gradientY + Mth.SmoothstepDerivative(y) * slopeY;
        derivatives[2] += gradientZ + Mth.SmoothstepDerivative(z) * slopeZ;

        return Mth.Lerp3(tx, ty, tz, d000, d100, d010, d110, d001, d101, d011, d111);
    }
}
