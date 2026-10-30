namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// <c>lerp</c>: from <c>first</c> to <c>second</c> by <c>alpha</c>. An alpha of exactly 0 or 1 returns that side
/// without sampling the other at a point, like vanilla's LerpFunction samplers.
/// </summary>
internal sealed class LerpFunction(DensityFunction alpha, DensityFunction first, DensityFunction second)
    : DensityFunction
{
    public override float Sample(int x, int y, int z)
    {
        var amount = alpha.Sample(x, y, z);

        if (amount == 0.0f)
            return first.Sample(x, y, z);

        if (amount == 1.0f)
            return second.Sample(x, y, z);

        var start = first.Sample(x, y, z);
        var end = second.Sample(x, y, z);

        return FloatMath.Lerp(amount, start, end);
    }

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        alpha.SampleVolume(output, volume);
        using var starts = DensityBuffer.Sample(first, volume);
        using var ends = DensityBuffer.Sample(second, volume);

        for (var i = 0; i < output.Length; i++)
        {
            var amount = output[i];

            if (amount == 0.0f)
                output[i] = starts.Span[i];
            else if (amount == 1.0f)
                output[i] = ends.Span[i];
            else
                output[i] = FloatMath.Lerp(amount, starts.Span[i], ends.Span[i]);
        }
    }
}

/// <summary><c>clamp</c>: <c>input</c> clamped to <c>min</c> and <c>max</c>.</summary>
internal sealed class ClampFunction(DensityFunction input, float min, float max) : DensityFunction
{
    public override float Sample(int x, int y, int z) => FloatMath.Clamp(input.Sample(x, y, z), min, max);

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        input.SampleVolume(output, volume);

        for (var i = 0; i < output.Length; i++)
            output[i] = FloatMath.Clamp(output[i], min, max);
    }
}

/// <summary>
/// <c>range_choice</c>: <c>when_in_range</c> where <c>input</c> is from <c>min_inclusive</c> up to
/// <c>max_exclusive</c>, otherwise <c>when_out_of_range</c>.
/// </summary>
internal sealed class RangeChoiceFunction(
    DensityFunction input,
    float minInclusive,
    float maxExclusive,
    DensityFunction whenInRange,
    DensityFunction whenOutOfRange) : DensityFunction
{
    public override float Sample(int x, int y, int z)
    {
        var value = input.Sample(x, y, z);
        var chosen = this.InRange(value) ? whenInRange : whenOutOfRange;

        return chosen.Sample(x, y, z);
    }

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        whenInRange.SampleVolume(output, volume);
        using var inputs = DensityBuffer.Sample(input, volume);
        using var outside = DensityBuffer.Sample(whenOutOfRange, volume);

        for (var i = 0; i < output.Length; i++)
        {
            if (!this.InRange(inputs.Span[i]))
                output[i] = outside.Span[i];
        }
    }

    private bool InRange(float value) => value >= minInclusive && value < maxExclusive;
}

/// <summary>
/// <c>interval_select</c>: the first of <c>functions</c> whose threshold <c>input</c> is below, or the last one.
/// There is one function more than <c>thresholds</c>, which ascend.
/// </summary>
internal sealed class IntervalSelectFunction(
    DensityFunction input,
    ImmutableArray<float> thresholds,
    ImmutableArray<DensityFunction> functions) : DensityFunction
{
    public override float Sample(int x, int y, int z)
    {
        var value = input.Sample(x, y, z);
        return functions[this.Select(value)].Sample(x, y, z);
    }

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        input.SampleVolume(output, volume);

        var buffers = new DensityBuffer[functions.Length];
        try
        {
            for (var i = 0; i < functions.Length; i++)
                buffers[i] = DensityBuffer.Sample(functions[i], volume);

            for (var i = 0; i < output.Length; i++)
            {
                var selected = this.Select(output[i]);
                output[i] = buffers[selected].Span[i];
            }
        }
        finally
        {
            foreach (var buffer in buffers)
                buffer.Dispose();
        }
    }

    private int Select(float value)
    {
        for (var i = 0; i < thresholds.Length; i++)
        {
            if (value < thresholds[i])
                return i;
        }

        return functions.Length - 1;
    }
}

/// <summary>
/// <c>interpolated</c>: <c>input</c> sampled at the corners of cells <c>cell_size_xz</c> by <c>cell_size_y</c> blocks,
/// and trilinearly interpolated in between.
/// </summary>
/// <remarks>
/// At a point it matches vanilla's InterpolatedFunction: a cell corner is sampled directly, any other point samples
/// the eight corners as a volume and interpolates them in float. A volume aligned to the cells samples the input
/// directly, like vanilla's; vanilla interpolates other volumes cell by cell, which the terrain step of Phase 5
/// matches against chunk captures, so for now they are sampled point by point.
/// </remarks>
internal sealed class InterpolatedFunction(DensityFunction input, int cellSizeXz, int cellSizeY) : DensityFunction
{
    public override float Sample(int x, int y, int z)
    {
        var xInCell = FloatMath.FloorMod(x, cellSizeXz);
        var yInCell = FloatMath.FloorMod(y, cellSizeY);
        var zInCell = FloatMath.FloorMod(z, cellSizeXz);

        if (xInCell == 0 && yInCell == 0 && zInCell == 0)
            return input.Sample(x, y, z);

        var corners = new DensityVolume(
            2,
            2,
            2,
            x - xInCell,
            y - yInCell,
            z - zInCell,
            cellSizeXz,
            cellSizeY,
            cellSizeXz);

        using var values = DensityBuffer.Sample(input, corners);
        var corner = values.Span;

        return FloatMath.Lerp3(
            (float)xInCell / cellSizeXz,
            (float)yInCell / cellSizeY,
            (float)zInCell / cellSizeXz,
            corner[corners.Index(0, 0, 0)],
            corner[corners.Index(1, 0, 0)],
            corner[corners.Index(0, 1, 0)],
            corner[corners.Index(1, 1, 0)],
            corner[corners.Index(0, 0, 1)],
            corner[corners.Index(1, 0, 1)],
            corner[corners.Index(0, 1, 1)],
            corner[corners.Index(1, 1, 1)]);
    }

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        if (this.IsAligned(volume))
            input.SampleVolume(output, volume);
        else
            base.SampleVolume(output, volume);
    }

    private bool IsAligned(in DensityVolume volume)
    {
        var stepsMatch = (volume.StepX == cellSizeXz || volume.SizeX == 1)
            && (volume.StepY == cellSizeY || volume.SizeY == 1)
            && (volume.StepZ == cellSizeXz || volume.SizeZ == 1);

        return stepsMatch
            && FloatMath.FloorMod(volume.MinBlockX, cellSizeXz) == 0
            && FloatMath.FloorMod(volume.MinBlockY, cellSizeY) == 0
            && FloatMath.FloorMod(volume.MinBlockZ, cellSizeXz) == 0;
    }
}

/// <summary>
/// <c>slice</c>: <c>input</c> with one coordinate fixed. Nested x and z slices fix both, like vanilla's compiler.
/// </summary>
internal sealed class SliceFunction(DensityFunction input, int? fixedX, int? fixedY, int? fixedZ) : DensityFunction
{
    public static SliceFunction Create(DensityAxis axis, int coordinate, DensityFunction input)
    {
        return axis switch
        {
            DensityAxis.X => new SliceFunction(input, coordinate, null, null),
            DensityAxis.Y => new SliceFunction(input, null, coordinate, null),
            _ => new SliceFunction(input, null, null, coordinate)
        };
    }

    public override float Sample(int x, int y, int z) => input.Sample(fixedX ?? x, fixedY ?? y, fixedZ ?? z);

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        var sliced = volume with
        {
            SizeX = fixedX is null ? volume.SizeX : 1,
            SizeY = fixedY is null ? volume.SizeY : 1,
            SizeZ = fixedZ is null ? volume.SizeZ : 1,
            MinBlockX = fixedX ?? volume.MinBlockX,
            MinBlockY = fixedY ?? volume.MinBlockY,
            MinBlockZ = fixedZ ?? volume.MinBlockZ
        };

        if (sliced == volume)
        {
            input.SampleVolume(output, volume);
            return;
        }

        using var values = DensityBuffer.Sample(input, sliced);
        var index = 0;

        for (var indexZ = 0; indexZ < volume.SizeZ; indexZ++)
        {
            var slicedZ = fixedZ is null ? indexZ : 0;

            for (var indexX = 0; indexX < volume.SizeX; indexX++)
            {
                var slicedX = fixedX is null ? indexX : 0;

                for (var indexY = 0; indexY < volume.SizeY; indexY++)
                {
                    var slicedY = fixedY is null ? indexY : 0;
                    output[index] = values.Span[sliced.Index(slicedX, slicedY, slicedZ)];
                    index++;
                }
            }
        }
    }
}

/// <summary>
/// <c>find_top_surface</c>: the highest y, stepping down from <c>upper_bound</c> in multiples of <c>cell_height</c>
/// to <c>lower_bound</c>, where <c>density</c> is positive. Per column: vanilla samples the bound at y = 0.
/// </summary>
internal sealed class FindTopSurfaceFunction(
    DensityFunction density,
    DensityFunction upperBound,
    int lowerBound,
    int cellHeight) : DensityFunction
{
    public override float Sample(int x, int y, int z) => this.FindSurface(x, z, upperBound.Sample(x, 0, z));

    // Over a volume vanilla samples the bounds as a volume too, which can round differently from a point.
    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        var columns = volume with
        {
            SizeY = 1,
            MinBlockY = 0
        };

        using var bounds = DensityBuffer.Sample(upperBound, columns);

        for (var indexZ = 0; indexZ < volume.SizeZ; indexZ++)
        {
            var z = volume.BlockZ(indexZ);

            for (var indexX = 0; indexX < volume.SizeX; indexX++)
            {
                var bound = bounds.Span[columns.Index(indexX, 0, indexZ)];
                var surface = this.FindSurface(volume.BlockX(indexX), z, bound);

                output.Slice(volume.Index(indexX, 0, indexZ), volume.SizeY).Fill(surface);
            }
        }
    }

    private int FindSurface(int x, int z, float bound)
    {
        // A float division, then a floor, as in vanilla.
        var topY = (int)MathF.Floor(bound / cellHeight) * cellHeight;
        if (topY <= lowerBound)
            return lowerBound;

        for (var probeY = topY; probeY >= lowerBound; probeY -= cellHeight)
        {
            if (density.Sample(x, probeY, z) > 0.0f)
                return probeY;
        }

        return lowerBound;
    }
}
