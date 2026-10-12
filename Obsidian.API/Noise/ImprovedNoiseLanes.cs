using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Obsidian.API.Noise;

/// <summary>
/// An <see cref="ImprovedNoise"/> sampled at four positions at once, one per vector lane.
/// </summary>
/// <remarks>
/// Each lane does the arithmetic of <see cref="ImprovedNoise.Noise(double, double, double, double, double)"/> in the same
/// order, without fused multiply-adds, so it gives the same bits. Vanilla's noise is defined by that arithmetic, which is
/// what world parity needs. Positions must be wrapped like <see cref="PerlinNoise.Wrap"/> does. Only on x86 with AVX
/// (see <see cref="IsSupported"/>); callers keep the scalar path for the rest.
/// </remarks>
internal sealed class ImprovedNoiseLanes
{
    // The components of ImprovedNoise's 16 gradients, each plus one so they're bytes.
    private static readonly Vector128<byte> gradientsX = Vector128.Create((byte)2, 0, 2, 0, 2, 0, 2, 0, 1, 1, 1, 1, 2, 1, 0, 1);
    private static readonly Vector128<byte> gradientsY = Vector128.Create((byte)2, 2, 0, 0, 1, 1, 1, 1, 2, 0, 2, 0, 2, 0, 2, 0);
    private static readonly Vector128<byte> gradientsZ = Vector128.Create((byte)1, 1, 1, 1, 2, 2, 0, 0, 2, 2, 0, 0, 1, 2, 1, 0);

    private readonly byte[] permutation;
    private readonly Vector256<double> xo;
    private readonly Vector256<double> yo;
    private readonly Vector256<double> zo;

    public static bool IsSupported => Avx.IsSupported && Ssse3.IsSupported;

    public ImprovedNoiseLanes(ImprovedNoise noise)
    {
        this.permutation = noise.Permutation;
        this.xo = Vector256.Create(noise.Xo);
        this.yo = Vector256.Create(noise.Yo);
        this.zo = Vector256.Create(noise.Zo);
    }

    /// <summary>
    /// <see cref="ImprovedNoise.Noise(double, double, double)"/> in each lane.
    /// </summary>
    public Vector256<double> Noise(Vector256<double> x, Vector256<double> y, Vector256<double> z)
    {
        var shiftedY = y + this.yo;
        var floorY = Vector256.Floor(shiftedY);
        var localY = shiftedY - floorY;
        return this.Sample(x, floorY, z, localY, localY);
    }

    /// <summary>
    /// <see cref="ImprovedNoise.Noise(double, double, double, double, double)"/> in each lane, with every lane's
    /// <paramref name="yScale"/> other than 0.
    /// </summary>
    /// <returns>False when a lane's smearing leaves the int range, which only the scalar path saturates like vanilla.</returns>
    public bool TryNoise(Vector256<double> x, Vector256<double> y, Vector256<double> z, Vector256<double> yScale, Vector256<double> yMax,
        out Vector256<double> noise)
    {
        var shiftedY = y + this.yo;
        var floorY = Vector256.Floor(shiftedY);
        var localY = shiftedY - floorY;

        var useMax = Vector256.GreaterThanOrEqual(yMax, Vector256<double>.Zero) & Vector256.LessThan(yMax, localY);
        var clampedY = Vector256.ConditionalSelect(useMax, yMax, localY);
        var steps = Vector256.Floor(clampedY / yScale + Vector256.Create(ImprovedNoise.ShiftUpEpsilon));

        // Also false for NaN.
        if (!Vector256.LessThanOrEqualAll(Vector256.Abs(steps), Vector256.Create((double)int.MaxValue)))
        {
            noise = default;
            return false;
        }

        noise = this.Sample(x, floorY, z, localY - steps * yScale, localY);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe Vector256<double> Sample(Vector256<double> x, Vector256<double> floorY, Vector256<double> z, Vector256<double> localY,
        Vector256<double> yForSmoothing)
    {
        var shiftedX = x + this.xo;
        var shiftedZ = z + this.zo;
        var floorX = Vector256.Floor(shiftedX);
        var floorZ = Vector256.Floor(shiftedZ);
        var localX = shiftedX - floorX;
        var localZ = shiftedZ - floorZ;

        // Wrapped positions floor well inside the int range.
        var cellX = Avx.ConvertToVector128Int32WithTruncation(floorX);
        var cellY = Avx.ConvertToVector128Int32WithTruncation(floorY);
        var cellZ = Avx.ConvertToVector128Int32WithTruncation(floorZ);

        fixed (byte* permutation = this.permutation)
        {
            var one = Vector128<int>.One;
            var a = P(permutation, cellX);
            var b = P(permutation, cellX + one);
            var aa = P(permutation, a + cellY);
            var ab = P(permutation, a + cellY + one);
            var ba = P(permutation, b + cellY);
            var bb = P(permutation, b + cellY + one);

            var localX1 = localX - Vector256<double>.One;
            var localY1 = localY - Vector256<double>.One;
            var localZ1 = localZ - Vector256<double>.One;

            var d000 = GradDot(P(permutation, aa + cellZ), localX, localY, localZ);
            var d100 = GradDot(P(permutation, ba + cellZ), localX1, localY, localZ);
            var d010 = GradDot(P(permutation, ab + cellZ), localX, localY1, localZ);
            var d110 = GradDot(P(permutation, bb + cellZ), localX1, localY1, localZ);
            var d001 = GradDot(P(permutation, aa + cellZ + one), localX, localY, localZ1);
            var d101 = GradDot(P(permutation, ba + cellZ + one), localX1, localY, localZ1);
            var d011 = GradDot(P(permutation, ab + cellZ + one), localX, localY1, localZ1);
            var d111 = GradDot(P(permutation, bb + cellZ + one), localX1, localY1, localZ1);

            var tx = Smoothstep(localX);
            var ty = Smoothstep(yForSmoothing);
            var tz = Smoothstep(localZ);

            // Mth.Lerp3's order.
            var bottom = Lerp(ty, Lerp(tx, d000, d100), Lerp(tx, d010, d110));
            var top = Lerp(ty, Lerp(tx, d001, d101), Lerp(tx, d011, d111));
            return Lerp(tz, bottom, top);
        }
    }

    // Each lane's permutation entry, at its index masked to 0-255 like ImprovedNoise. Looking the entries up one at a time
    // is faster than gathering them.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Vector128<int> P(byte* permutation, Vector128<int> index)
    {
        var masked = index & Vector128.Create(0xFF);
        return Vector128.Create(permutation[masked.GetElement(0)], permutation[masked.GetElement(1)], permutation[masked.GetElement(2)],
            permutation[masked.GetElement(3)]);
    }

    // SimplexNoise.Dot: the hash picks a gradient whose components are -1, 0 or 1, as exact doubles.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> GradDot(Vector128<int> hash, Vector256<double> x, Vector256<double> y, Vector256<double> z)
    {
        // Each int lane's low byte indexes the tables; the 0x80 bytes above it look up nothing, so the lane holds the byte.
        var index = ((hash & Vector128.Create(15)) | Vector128.Create(unchecked((int)0x80808000))).AsByte();
        var gradientX = Avx.ConvertToVector256Double(Ssse3.Shuffle(gradientsX, index).AsInt32()) - Vector256<double>.One;
        var gradientY = Avx.ConvertToVector256Double(Ssse3.Shuffle(gradientsY, index).AsInt32()) - Vector256<double>.One;
        var gradientZ = Avx.ConvertToVector256Double(Ssse3.Shuffle(gradientsZ, index).AsInt32()) - Vector256<double>.One;
        return gradientX * x + gradientY * y + gradientZ * z;
    }

    // Mth.Smoothstep.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> Smoothstep(Vector256<double> t) =>
        t * t * t * (t * (t * Vector256.Create(6.0) - Vector256.Create(15.0)) + Vector256.Create(10.0));

    // Mth.Lerp.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> Lerp(Vector256<double> delta, Vector256<double> start, Vector256<double> end) =>
        start + delta * (end - start);
}
