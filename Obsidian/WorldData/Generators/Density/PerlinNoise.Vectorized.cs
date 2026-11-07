using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Obsidian.WorldData.Generators.Density;

// Eight points at once: every density volume samples its noises over many points.
internal partial class PerlinNoise
{
    // Each direction component is stored in two bits as component + 1, so a plane's four gradients fit in an int.
    private static readonly int[] packedGradients = Array.ConvertAll(
        gradients,
        gradient => (gradient.X + 1) | ((gradient.Y + 1) << 2) | ((gradient.Z + 1) << 4));

    // The gradients' components for hashes 0-7 and 8-15, picked by permuting a vector rather than loading per point.
    private static readonly Vector256<float> gradientXLow = GradientComponents(0, 0);
    private static readonly Vector256<float> gradientXHigh = GradientComponents(0, 8);
    private static readonly Vector256<float> gradientYLow = GradientComponents(1, 0);
    private static readonly Vector256<float> gradientYHigh = GradientComponents(1, 8);
    private static readonly Vector256<float> gradientZLow = GradientComponents(2, 0);
    private static readonly Vector256<float> gradientZHigh = GradientComponents(2, 8);

    /// <summary>Whether <see cref="Get8"/> can run on this machine.</summary>
    public static bool CanSampleEight => Avx2.IsSupported;

    /// <summary>
    /// The noise at eight points, each exactly as <see cref="Get"/> samples it. Each coordinate comes as two vectors:
    /// the first four points, then the last four. Needs <see cref="CanSampleEight"/>.
    /// </summary>
    /// <remarks>
    /// Every step is the scalar path's IEEE operation in the same order, on each lane: flooring, truncating to int,
    /// narrowing to float, and float multiplications and additions that are never fused.
    /// </remarks>
    public Vector256<float> Get8(
        Vector256<double> xLow,
        Vector256<double> xHigh,
        Vector256<double> yLow,
        Vector256<double> yHigh,
        Vector256<double> zLow,
        Vector256<double> zHigh)
    {
        var offsetX = Vector256.Create(this.OffsetX);
        var offsetY = Vector256.Create(this.OffsetY);
        var offsetZ = Vector256.Create(this.OffsetZ);

        var cellX = Cell(Wrap(xLow) + offsetX, Wrap(xHigh) + offsetX, out var relativeX);
        var cellZ = Cell(Wrap(zLow) + offsetZ, Wrap(zHigh) + offsetZ, out var relativeZ);

        var wrappedYLow = Wrap(yLow) + offsetY;
        var wrappedYHigh = Wrap(yHigh) + offsetY;
        var floorYLow = Floor(wrappedYLow, out var cellYLow);
        var floorYHigh = Floor(wrappedYHigh, out var cellYHigh);
        var relativeYLow = wrappedYLow - floorYLow;
        var relativeYHigh = wrappedYHigh - floorYHigh;
        var cellY = Vector256.Create(cellYLow, cellYHigh);
        var fadeY = Narrow(relativeYLow, relativeYHigh);

        if (this.smearScaleY == 0.0)
            return this.SampleAndLerp8(cellX, cellY, cellZ, relativeX, fadeY, relativeZ, fadeY);

        var cornerY = Narrow(this.Smear(relativeYLow, yLow), this.Smear(relativeYHigh, yHigh));

        return this.SampleAndLerp8(cellX, cellY, cellZ, relativeX, cornerY, relativeZ, fadeY);
    }

    /// <summary>
    /// The noise at eight points of a column, each exactly as <see cref="Get"/> samples it: x and z are the same for
    /// all of them, and y comes as two vectors, the first four points, then the last four. Needs
    /// <see cref="CanSampleEight"/>.
    /// </summary>
    /// <remarks>
    /// The x and z lattice positions are worked out once. When all eight y fall in one lattice cell, as they mostly do
    /// at low frequencies, the cell's eight corner gradients are looked up once and shared by every point, rather than
    /// gathered and picked per point. Columns spanning up to eight adjacent cells share a table of their y planes.
    /// The arithmetic on each lane is the same in every path.
    /// </remarks>
    public Vector256<float> GetColumn8(double x, double z, Vector256<double> yLow, Vector256<double> yHigh)
    {
        var wrappedX = Wrap(x) + this.OffsetX;
        var wrappedZ = Wrap(z) + this.OffsetZ;
        var cellX = (int)Math.Floor(wrappedX);
        var cellZ = (int)Math.Floor(wrappedZ);
        var relativeX = (float)(wrappedX - cellX);
        var relativeZ = (float)(wrappedZ - cellZ);

        var offsetY = Vector256.Create(this.OffsetY);
        var wrappedYLow = Wrap(yLow) + offsetY;
        var wrappedYHigh = Wrap(yHigh) + offsetY;
        var floorYLow = Floor(wrappedYLow, out var cellYLow);
        var floorYHigh = Floor(wrappedYHigh, out var cellYHigh);
        var relativeYLow = wrappedYLow - floorYLow;
        var relativeYHigh = wrappedYHigh - floorYHigh;
        var cellY = Vector256.Create(cellYLow, cellYHigh);
        var fadeY = Narrow(relativeYLow, relativeYHigh);

        var cornerY = this.smearScaleY == 0.0
            ? fadeY
            : Narrow(this.Smear(relativeYLow, yLow), this.Smear(relativeYHigh, yHigh));

        var firstCellY = cellY.ToScalar();
        if (Vector256.EqualsAll(cellY, Vector256.Create(firstCellY)))
            return this.SampleCell8(cellX, firstCellY, cellZ, relativeX, cornerY, relativeZ, fadeY);

        var lastCellY = cellY.GetElement(7);
        var minimumY = Math.Min(firstCellY, lastCellY);
        var cellSpan = Math.Abs(lastCellY - firstCellY);
        var planeIndices = cellY - Vector256.Create(minimumY);

        // Endpoints bound a monotone column. Check every lane too: selected points need not be consecutive,
        // and wrapping or an arbitrary caller can put an interior point outside the endpoints.
        if (cellSpan <= 7
            && Vector256.LessThanOrEqual(planeIndices.AsUInt32(), Vector256.Create((uint)cellSpan))
                == Vector256<uint>.AllBitsSet)
        {
            return this.SampleColumnCells8(
                cellX, minimumY, cellZ, cellSpan, planeIndices, relativeX, cornerY, relativeZ, fadeY);
        }

        return this.SampleAndLerp8(
            Vector256.Create(cellX),
            cellY,
            Vector256.Create(cellZ),
            Vector256.Create(relativeX),
            cornerY,
            Vector256.Create(relativeZ),
            fadeY);
    }

    // SampleAndLerp8 for eight points in one lattice cell, which share its corner hashes, x and z.
    private Vector256<float> SampleCell8(
        int x,
        int y,
        int z,
        float relativeX,
        Vector256<float> relativeY,
        float relativeZ,
        Vector256<float> fadeY)
    {
        ref var permutations = ref this.Permutations;
        var x0 = Permute(ref permutations, x);
        var x1 = Permute(ref permutations, x + 1);
        var xy00 = Permute(ref permutations, x0 + y);
        var xy01 = Permute(ref permutations, x0 + y + 1);
        var xy10 = Permute(ref permutations, x1 + y);
        var xy11 = Permute(ref permutations, x1 + y + 1);

        var nearX = Vector256.Create(relativeX);
        var nearZ = Vector256.Create(relativeZ);
        var farX = nearX - Vector256<float>.One;
        var farY = relativeY - Vector256<float>.One;
        var farZ = nearZ - Vector256<float>.One;

        var d000 = DotCell8(Permute(ref permutations, xy00 + z), nearX, relativeY, nearZ);
        var d100 = DotCell8(Permute(ref permutations, xy10 + z), farX, relativeY, nearZ);
        var d010 = DotCell8(Permute(ref permutations, xy01 + z), nearX, farY, nearZ);
        var d110 = DotCell8(Permute(ref permutations, xy11 + z), farX, farY, nearZ);
        var d001 = DotCell8(Permute(ref permutations, xy00 + z + 1), nearX, relativeY, farZ);
        var d101 = DotCell8(Permute(ref permutations, xy10 + z + 1), farX, relativeY, farZ);
        var d011 = DotCell8(Permute(ref permutations, xy01 + z + 1), nearX, farY, farZ);
        var d111 = DotCell8(Permute(ref permutations, xy11 + z + 1), farX, farY, farZ);

        var alphaX = Smoothstep8(nearX);
        var alphaY = Smoothstep8(fadeY);
        var alphaZ = Smoothstep8(nearZ);

        var bottom = Lerp8(alphaY, Lerp8(alphaX, d000, d100), Lerp8(alphaX, d010, d110));
        var top = Lerp8(alphaY, Lerp8(alphaX, d001, d101), Lerp8(alphaX, d011, d111));

        return Lerp8(alphaZ, bottom, top);
    }

    // A small column table replaces fourteen dependent permutation gathers with two plane gathers.
    // Adjacent cells share their boundary plane; a plane stores four packed gradient directions.
    private unsafe Vector256<float> SampleColumnCells8(
        int x,
        int minimumY,
        int z,
        int cellSpan,
        Vector256<int> planeIndices,
        float relativeX,
        Vector256<float> relativeY,
        float relativeZ,
        Vector256<float> fadeY)
    {
        ref var permutations = ref this.Permutations;
        var x0 = Permute(ref permutations, x);
        var x1 = Permute(ref permutations, x + 1);
        int* planes = stackalloc int[9];

        for (var plane = 0; plane <= cellSpan + 1; plane++)
        {
            var y = minimumY + plane;
            var xy0 = Permute(ref permutations, x0 + y);
            var xy1 = Permute(ref permutations, x1 + y);
            var near00 = packedGradients[Permute(ref permutations, xy0 + z) & 15];
            var near10 = packedGradients[Permute(ref permutations, xy1 + z) & 15];
            var far00 = packedGradients[Permute(ref permutations, xy0 + z + 1) & 15];
            var far10 = packedGradients[Permute(ref permutations, xy1 + z + 1) & 15];
            planes[plane] = near00 | (near10 << 6) | (far00 << 12) | (far10 << 18);
        }

        var lower = Avx2.GatherVector256(planes, planeIndices, 4);
        var upper = Avx2.GatherVector256(planes, planeIndices + Vector256<int>.One, 4);
        var nearX = Vector256.Create(relativeX);
        var nearZ = Vector256.Create(relativeZ);
        var farX = nearX - Vector256<float>.One;
        var farY = relativeY - Vector256<float>.One;
        var farZ = nearZ - Vector256<float>.One;

        var d000 = DotPacked8(lower, nearX, relativeY, nearZ);
        var d100 = DotPacked8(lower >> 6, farX, relativeY, nearZ);
        var d010 = DotPacked8(upper, nearX, farY, nearZ);
        var d110 = DotPacked8(upper >> 6, farX, farY, nearZ);
        var d001 = DotPacked8(lower >> 12, nearX, relativeY, farZ);
        var d101 = DotPacked8(lower >> 18, farX, relativeY, farZ);
        var d011 = DotPacked8(upper >> 12, nearX, farY, farZ);
        var d111 = DotPacked8(upper >> 18, farX, farY, farZ);

        // Vanilla 26.3 evaluates gradient products and lerps in float in this order, without fused operations.
        // Only the direction lookup changes; even zero components must still multiply their coordinate.
        var alphaX = Smoothstep8(nearX);
        var alphaY = Smoothstep8(fadeY);
        var alphaZ = Smoothstep8(nearZ);
        var bottom = Lerp8(alphaY, Lerp8(alphaX, d000, d100), Lerp8(alphaX, d010, d110));
        var top = Lerp8(alphaY, Lerp8(alphaX, d001, d101), Lerp8(alphaX, d011, d111));

        return Lerp8(alphaZ, bottom, top);
    }

    private static Vector256<float> DotPacked8(
        Vector256<int> packed,
        Vector256<float> x,
        Vector256<float> y,
        Vector256<float> z)
    {
        var mask = Vector256.Create(3);
        var gradientX = Vector256.ConvertToSingle((packed & mask) - Vector256<int>.One);
        var gradientY = Vector256.ConvertToSingle(((packed >> 2) & mask) - Vector256<int>.One);
        var gradientZ = Vector256.ConvertToSingle(((packed >> 4) & mask) - Vector256<int>.One);

        return gradientX * x + gradientY * y + gradientZ * z;
    }

    // Dot8 with one hash for every lane.
    private static Vector256<float> DotCell8(int hash, Vector256<float> x, Vector256<float> y, Vector256<float> z)
    {
        var gradient = gradients[hash & 15];

        return Vector256.Create((float)gradient.X) * x
            + Vector256.Create((float)gradient.Y) * y
            + Vector256.Create((float)gradient.Z) * z;
    }

    // SampleSmeared's quantised y, before narrowing.
    private Vector256<double> Smear(Vector256<double> relativeY, Vector256<double> originalY)
    {
        var scale = Vector256.Create(this.smearScaleY);
        var inCell = Vector256.GreaterThanOrEqual(originalY, Vector256<double>.Zero)
            & Vector256.LessThan(originalY, relativeY);
        var fudgeLimit = Vector256.ConditionalSelect(inCell, originalY, relativeY);
        var steps = Avx.RoundToNegativeInfinity(fudgeLimit / scale + Vector256.Create((double)1.0E-7f));

        return relativeY - steps * scale;
    }

    private unsafe Vector256<float> SampleAndLerp8(
        Vector256<int> x,
        Vector256<int> y,
        Vector256<int> z,
        Vector256<float> relativeX,
        Vector256<float> relativeY,
        Vector256<float> relativeZ,
        Vector256<float> fadeY)
    {
        var one = Vector256<int>.One;
        Vector256<int> hash000, hash100, hash010, hash110, hash001, hash101, hash011, hash111;

        fixed (int* permutations = this.permutationTable)
        {
            var x0 = Permute8(permutations, x);
            var x1 = Permute8(permutations, x + one);
            var xy00 = Permute8(permutations, x0 + y);
            var xy01 = Permute8(permutations, x0 + y + one);
            var xy10 = Permute8(permutations, x1 + y);
            var xy11 = Permute8(permutations, x1 + y + one);

            hash000 = Permute8(permutations, xy00 + z);
            hash100 = Permute8(permutations, xy10 + z);
            hash010 = Permute8(permutations, xy01 + z);
            hash110 = Permute8(permutations, xy11 + z);
            hash001 = Permute8(permutations, xy00 + z + one);
            hash101 = Permute8(permutations, xy10 + z + one);
            hash011 = Permute8(permutations, xy01 + z + one);
            hash111 = Permute8(permutations, xy11 + z + one);
        }

        var ones = Vector256<float>.One;
        var farX = relativeX - ones;
        var farY = relativeY - ones;
        var farZ = relativeZ - ones;

        var d000 = Dot8(hash000, relativeX, relativeY, relativeZ);
        var d100 = Dot8(hash100, farX, relativeY, relativeZ);
        var d010 = Dot8(hash010, relativeX, farY, relativeZ);
        var d110 = Dot8(hash110, farX, farY, relativeZ);
        var d001 = Dot8(hash001, relativeX, relativeY, farZ);
        var d101 = Dot8(hash101, farX, relativeY, farZ);
        var d011 = Dot8(hash011, relativeX, farY, farZ);
        var d111 = Dot8(hash111, farX, farY, farZ);

        var alphaX = Smoothstep8(relativeX);
        var alphaY = Smoothstep8(fadeY);
        var alphaZ = Smoothstep8(relativeZ);

        var bottom = Lerp8(alphaY, Lerp8(alphaX, d000, d100), Lerp8(alphaX, d010, d110));
        var top = Lerp8(alphaY, Lerp8(alphaX, d001, d101), Lerp8(alphaX, d011, d111));

        return Lerp8(alphaZ, bottom, top);
    }

    // Wrap, for four coordinates; most are already in range.
    private static Vector256<double> Wrap(Vector256<double> value)
    {
        var half = Vector256.Create(halfWrapPeriod);
        var inRange = Vector256.GreaterThanOrEqual(value, -half) & Vector256.LessThan(value, half);

        if (inRange.AsInt64() == Vector256<long>.AllBitsSet)
            return value;

        var period = Vector256.Create(WrapPeriod);
        var periods = Avx.RoundToNegativeInfinity(value / period + Vector256.Create(0.5));
        var wrapped = value - periods * period;

        return Vector256.ConditionalSelect(inRange, value, wrapped);
    }

    // The int floor of each coordinate, and the floor as the scalar path converts it back to double.
    private static Vector256<double> Floor(Vector256<double> value, out Vector128<int> cell)
    {
        cell = Avx.ConvertToVector128Int32WithTruncation(Avx.RoundToNegativeInfinity(value));
        return Avx.ConvertToVector256Double(cell);
    }

    // The lattice cell of eight coordinates and the narrowed position inside it.
    private static Vector256<int> Cell(Vector256<double> low, Vector256<double> high, out Vector256<float> relative)
    {
        var floorLow = Floor(low, out var cellLow);
        var floorHigh = Floor(high, out var cellHigh);
        relative = Narrow(low - floorLow, high - floorHigh);

        return Vector256.Create(cellLow, cellHigh);
    }

    private static Vector256<float> Narrow(Vector256<double> low, Vector256<double> high) =>
        Vector256.Create(Avx.ConvertToVector128Single(low), Avx.ConvertToVector128Single(high));

    // Permute's lookups, masked into the 256 values.
    private static unsafe Vector256<int> Permute8(int* permutations, Vector256<int> value) =>
        Avx2.GatherVector256(permutations, value & Vector256.Create(0xFF), 4);

    // Dot's sum, in its order, with each hash's gradient picked from the eight low or the eight high ones.
    private static Vector256<float> Dot8(
        Vector256<int> hash,
        Vector256<float> x,
        Vector256<float> y,
        Vector256<float> z)
    {
        var index = hash & Vector256.Create(15);
        var high = Avx2.ShiftLeftLogical(index, 28).AsSingle();
        var gradientX = Pick(gradientXLow, gradientXHigh, index, high);
        var gradientY = Pick(gradientYLow, gradientYHigh, index, high);
        var gradientZ = Pick(gradientZLow, gradientZHigh, index, high);

        return gradientX * x + gradientY * y + gradientZ * z;
    }

    // The low eight's or the high eight's entry, by the index's bit 3 shifted to the sign.
    private static Vector256<float> Pick(Vector256<float> low, Vector256<float> high, Vector256<int> index,
        Vector256<float> highMask)
    {
        var fromLow = Avx2.PermuteVar8x32(low, index);
        var fromHigh = Avx2.PermuteVar8x32(high, index);

        return Avx.BlendVariable(fromLow, fromHigh, highMask);
    }

    private static Vector256<float> Smoothstep8(Vector256<float> x)
    {
        var inner = x * (x * Vector256.Create(6.0f) - Vector256.Create(15.0f)) + Vector256.Create(10.0f);
        return x * x * x * inner;
    }

    private static Vector256<float> Lerp8(Vector256<float> alpha, Vector256<float> start, Vector256<float> end) =>
        start + alpha * (end - start);

    private static Vector256<float> GradientComponents(int component, int firstHash)
    {
        Span<float> values = stackalloc float[8];

        for (var i = 0; i < 8; i++)
        {
            var gradient = gradients[firstHash + i];
            values[i] = component switch
            {
                0 => gradient.X,
                1 => gradient.Y,
                _ => gradient.Z
            };
        }

        return Vector256.Create<float>(values);
    }
}
