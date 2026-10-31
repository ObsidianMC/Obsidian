using System.Buffers;

namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// <c>lerp</c>: from <c>first</c> to <c>second</c> by <c>alpha</c>. An alpha of exactly 0 or 1 returns that side
/// without sampling the other at a point, like vanilla's LerpFunction samplers.
/// </summary>
internal sealed class LerpFunction(DensityFunction alpha, DensityFunction first, DensityFunction second)
    : DensityFunction
{
    public override bool IsFlat => alpha.IsFlat && first.IsFlat && second.IsFlat;

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
        this.SampleBranches(output, volume, []);
    }

    public override void SampleSelected(Span<float> output, in DensityVolume volume, ReadOnlySpan<int> indices)
    {
        alpha.SampleSelected(output, volume, indices);
        this.SampleBranches(output, volume, indices);
    }

    private void SampleBranches(Span<float> output, in DensityVolume volume, ReadOnlySpan<int> indices)
    {
        if (!output.ContainsAnyExcept(0.0f))
        {
            first.SampleSelection(output, volume, indices);
            return;
        }

        if (!output.ContainsAnyExcept(1.0f))
        {
            second.SampleSelection(output, volume, indices);
            return;
        }

        using var starts = new DensityBuffer(output.Length);
        using var ends = new DensityBuffer(output.Length);

        using (var selection = new DensitySelection(output, indices, DensitySelectionKind.NotOne))
            selection.Sample(first, starts.Span, volume);

        using (var selection = new DensitySelection(output, indices, DensitySelectionKind.NotZero))
            selection.Sample(second, ends.Span, volume);

        var startValues = starts.Span;
        var endValues = ends.Span;

        for (var i = 0; i < output.Length; i++)
        {
            var amount = output[i];

            if (amount == 0.0f)
                output[i] = startValues[i];
            else if (amount == 1.0f)
                output[i] = endValues[i];
            else
                output[i] = FloatMath.Lerp(amount, startValues[i], endValues[i]);
        }
    }
}

/// <summary><c>clamp</c>: <c>input</c> clamped to <c>min</c> and <c>max</c>.</summary>
internal sealed class ClampFunction(DensityFunction input, float min, float max) : DensityFunction
{
    public override bool IsFlat => input.IsFlat;

    public override bool SamplesCheaply => input.SamplesCheaply;

    public override float Sample(int x, int y, int z) => FloatMath.Clamp(input.Sample(x, y, z), min, max);

    public override void SampleSelected(Span<float> output, in DensityVolume volume, ReadOnlySpan<int> indices)
    {
        input.SampleSelected(output, volume, indices);
        DensityLoops.Clamp(output, min, max);
    }

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        input.SampleVolume(output, volume);
        DensityLoops.Clamp(output, min, max);
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
    private readonly bool cheapBranches = whenInRange.SamplesCheaply && whenOutOfRange.SamplesCheaply;

    public override bool IsFlat => input.IsFlat && whenInRange.IsFlat && whenOutOfRange.IsFlat;

    public override bool SamplesCheaply => input.SamplesCheaply && this.cheapBranches;

    public override float Sample(int x, int y, int z)
    {
        var value = input.Sample(x, y, z);
        var chosen = this.InRange(value) ? whenInRange : whenOutOfRange;

        return chosen.Sample(x, y, z);
    }

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        using var inputs = DensityBuffer.Sample(input, volume);
        this.SampleBranches(output, inputs.Span, volume, []);
    }

    public override void SampleSelected(Span<float> output, in DensityVolume volume, ReadOnlySpan<int> indices)
    {
        using var inputs = new DensityBuffer(indices.Length);
        input.SampleSelected(inputs.Span, volume, indices);
        this.SampleBranches(output, inputs.Span, volume, indices);
    }

    private void SampleBranches(
        Span<float> output,
        ReadOnlySpan<float> controls,
        in DensityVolume volume,
        ReadOnlySpan<int> indices)
    {
        var inRange = 0;
        foreach (var value in controls)
        {
            if (this.InRange(value))
                inRange++;
        }

        if (inRange == controls.Length)
        {
            whenInRange.SampleSelection(output, volume, indices);
            return;
        }

        if (inRange == 0)
        {
            whenOutOfRange.SampleSelection(output, volume, indices);
            return;
        }

        if (this.cheapBranches)
        {
            this.SampleBothBranches(output, controls, volume, indices);
            return;
        }

        using (var selection = new DensitySelection(
            controls, indices, DensitySelectionKind.InRange, minInclusive, maxExclusive))
        {
            selection.Sample(whenInRange, output, volume);
        }

        using (var selection = new DensitySelection(
            controls, indices, DensitySelectionKind.OutOfRange, minInclusive, maxExclusive))
        {
            selection.Sample(whenOutOfRange, output, volume);
        }
    }

    // Samples both branches at every point and keeps each point's own. A branch's value at a point doesn't depend
    // on which other points it samples, so this only saves selecting the points (the noodle caves' branches).
    private void SampleBothBranches(
        Span<float> output,
        ReadOnlySpan<float> controls,
        in DensityVolume volume,
        ReadOnlySpan<int> indices)
    {
        using var outOfRange = new DensityBuffer(output.Length);
        var outOfRangeValues = outOfRange.Span;
        whenOutOfRange.SampleSelection(outOfRangeValues, volume, indices);
        whenInRange.SampleSelection(output, volume, indices);

        for (var i = 0; i < output.Length; i++)
        {
            if (!this.InRange(controls[i]))
                output[i] = outOfRangeValues[i];
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
    public override bool IsFlat => input.IsFlat && functions.All(function => function.IsFlat);

    public override float Sample(int x, int y, int z)
    {
        var value = input.Sample(x, y, z);
        return functions[this.Select(value)].Sample(x, y, z);
    }

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        using var controls = DensityBuffer.Sample(input, volume);
        this.SampleBranches(output, controls.Span, volume, []);
    }

    public override void SampleSelected(Span<float> output, in DensityVolume volume, ReadOnlySpan<int> indices)
    {
        using var controls = new DensityBuffer(indices.Length);
        input.SampleSelected(controls.Span, volume, indices);
        this.SampleBranches(output, controls.Span, volume, indices);
    }

    private void SampleBranches(
        Span<float> output,
        ReadOnlySpan<float> controls,
        in DensityVolume volume,
        ReadOnlySpan<int> indices)
    {
        for (var i = 0; i < functions.Length; i++)
        {
            var kind = DensitySelectionKind.InRange;
            var min = i == 0 ? 0.0f : thresholds[i - 1];
            var max = i == thresholds.Length ? 0.0f : thresholds[i];

            if (i == 0)
                kind = DensitySelectionKind.Below;
            else if (i == thresholds.Length)
                kind = DensitySelectionKind.NotBelow;

            using var selection = new DensitySelection(controls, indices, kind, min, max);
            selection.Sample(functions[i], output, volume);
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
/// <para>
/// At a point it matches vanilla's InterpolatedFunction: a cell corner is sampled directly, any other point samples
/// the eight corners as a volume and interpolates them in float.
/// </para>
/// <para>
/// A volume aligned to the cells samples the input directly. Any other volume is interpolated cell by cell like
/// vanilla's: each column steps up by a slope instead of interpolating every y, and a volume whose points are more
/// than a block apart (an outer interpolation's corners, say) is first interpolated at every block of its extent. Both
/// round differently from interpolating each point, so nested interpolations depend on them.
/// </para>
/// </remarks>
internal sealed class InterpolatedFunction(DensityFunction input, int cellSizeXz, int cellSizeY) : DensityFunction
{
    // Its input is sampled at cell corners only; each point costs a few float operations.
    public override bool SamplesCheaply => true;

    // Vanilla multiplies by these reciprocals over a volume, but divides at a point.
    private readonly float inverseCellSizeXz = 1.0f / cellSizeXz;
    private readonly float inverseCellSizeY = 1.0f / cellSizeY;

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
        {
            input.SampleVolume(output, volume);
            return;
        }

        var blockStep = volume.StepX == 1 && volume.StepY == 1 && volume.StepZ == 1;

        if (blockStep)
            this.SampleBlocks(output, volume);
        else
            this.SampleSteppedVolume(output, volume);
    }

    public override void SampleSelected(Span<float> output, in DensityVolume volume, ReadOnlySpan<int> indices)
    {
        if (this.IsAligned(volume))
        {
            input.SampleSelected(output, volume, indices);
            return;
        }

        // The full fill shares corner blends across columns; sparse coordinate decoding only pays for small subsets.
        if (indices.Length > volume.Size / 8)
        {
            base.SampleSelected(output, volume, indices);
            return;
        }

        var blocks = BlockVolume(volume);
        var corners = this.CornerVolume(blocks);
        using var values = new DensityBuffer(corners.Size);
        this.SampleSelectedCorners(values.Span, volume, corners, indices);
        var previousX = int.MinValue;
        var previousZ = int.MinValue;
        var previousCellY = int.MinValue;
        var previousY = 0;
        var value = 0.0f;
        var slope = 0.0f;

        for (var i = 0; i < indices.Length; i++)
        {
            volume.Position(indices[i], out var x, out var y, out var z);
            var cellY = (y - corners.MinBlockY) / cellSizeY;
            var localY = y - corners.BlockY(cellY);

            if (x != previousX || z != previousZ || cellY != previousCellY)
            {
                var cellX = (x - corners.MinBlockX) / cellSizeXz;
                var cellZ = (z - corners.MinBlockZ) / cellSizeXz;
                var nextX = Math.Min(cellX + 1, corners.SizeX - 1);
                var nextY = Math.Min(cellY + 1, corners.SizeY - 1);
                var nextZ = Math.Min(cellZ + 1, corners.SizeZ - 1);
                var alphaZ = (z - corners.BlockZ(cellZ)) * this.inverseCellSizeXz;
                var alphaX = (x - corners.BlockX(cellX)) * this.inverseCellSizeXz;
                var samples = values.Span;
                var x0y0 = FloatMath.Lerp(alphaZ,
                    samples[corners.Index(cellX, cellY, cellZ)], samples[corners.Index(cellX, cellY, nextZ)]);
                var x0y1 = FloatMath.Lerp(alphaZ,
                    samples[corners.Index(cellX, nextY, cellZ)], samples[corners.Index(cellX, nextY, nextZ)]);
                var x1y0 = FloatMath.Lerp(alphaZ,
                    samples[corners.Index(nextX, cellY, cellZ)], samples[corners.Index(nextX, cellY, nextZ)]);
                var x1y1 = FloatMath.Lerp(alphaZ,
                    samples[corners.Index(nextX, nextY, cellZ)], samples[corners.Index(nextX, nextY, nextZ)]);
                var bottom = FloatMath.Lerp(alphaX, x0y0, x1y0);
                var top = FloatMath.Lerp(alphaX, x0y1, x1y1);

                // Vanilla starts at the original volume's first block in this cell and adds the slope once per y.
                // Starting at the selected block, or multiplying across skipped blocks, changes the rounded value.
                slope = (top - bottom) * this.inverseCellSizeY;
                previousY = Math.Max(0, blocks.MinBlockY - corners.BlockY(cellY));
                value = bottom + slope * previousY;
                previousX = x;
                previousZ = z;
                previousCellY = cellY;
            }

            while (previousY < localY)
            {
                value += slope;
                previousY++;
            }

            output[i] = value;
        }
    }

    // A sparse ore mask may touch only a few cells. Keep the original corner volume for rounding, but sample
    // only corners those cells use, rather than evaluating their noises over the chunk's whole height.
    private void SampleSelectedCorners(
        Span<float> output,
        in DensityVolume volume,
        in DensityVolume corners,
        ReadOnlySpan<int> indices)
    {
        var marked = ArrayPool<byte>.Shared.Rent(corners.Size);
        var selected = ArrayPool<int>.Shared.Rent(corners.Size);

        try
        {
            marked.AsSpan(0, corners.Size).Clear();

            foreach (var index in indices)
            {
                volume.Position(index, out var x, out var y, out var z);
                var cellX = (x - corners.MinBlockX) / cellSizeXz;
                var cellY = (y - corners.MinBlockY) / cellSizeY;
                var cellZ = (z - corners.MinBlockZ) / cellSizeXz;
                var nextX = Math.Min(cellX + 1, corners.SizeX - 1);
                var nextY = Math.Min(cellY + 1, corners.SizeY - 1);
                var nextZ = Math.Min(cellZ + 1, corners.SizeZ - 1);

                marked[corners.Index(cellX, cellY, cellZ)] = 1;
                marked[corners.Index(cellX, nextY, cellZ)] = 1;
                marked[corners.Index(nextX, cellY, cellZ)] = 1;
                marked[corners.Index(nextX, nextY, cellZ)] = 1;
                marked[corners.Index(cellX, cellY, nextZ)] = 1;
                marked[corners.Index(cellX, nextY, nextZ)] = 1;
                marked[corners.Index(nextX, cellY, nextZ)] = 1;
                marked[corners.Index(nextX, nextY, nextZ)] = 1;
            }

            var count = 0;
            for (var i = 0; i < corners.Size; i++)
            {
                if (marked[i] != 0)
                    selected[count++] = i;
            }

            if (count == corners.Size)
            {
                input.SampleVolume(output, corners);
                return;
            }

            using var values = new DensityBuffer(count);
            input.SampleSelected(values.Span, corners, selected.AsSpan(0, count));

            for (var i = 0; i < count; i++)
                output[selected[i]] = values.Span[i];
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(marked);
            ArrayPool<int>.Shared.Return(selected);
        }
    }

    private static DensityVolume BlockVolume(in DensityVolume volume) => new(
        volume.SizeX * volume.StepX,
        volume.SizeY * volume.StepY,
        volume.SizeZ * volume.StepZ,
        volume.MinBlockX,
        volume.MinBlockY,
        volume.MinBlockZ);

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

    // Vanilla interpolates every block from the volume's first point up to a step past its last, then keeps the
    // points the volume steps on.
    private void SampleSteppedVolume(Span<float> output, in DensityVolume volume)
    {
        var blocks = BlockVolume(volume);

        using var values = new DensityBuffer(blocks.Size);
        this.SampleBlocks(values.Span, blocks);

        for (var indexZ = 0; indexZ < volume.SizeZ; indexZ++)
        {
            for (var indexX = 0; indexX < volume.SizeX; indexX++)
            {
                for (var indexY = 0; indexY < volume.SizeY; indexY++)
                {
                    var block = blocks.Index(indexX * volume.StepX, indexY * volume.StepY, indexZ * volume.StepZ);
                    output[volume.Index(indexX, indexY, indexZ)] = values.Span[block];
                }
            }
        }
    }

    // The original corner grid, including the shorter last cell when a volume ends exactly on a cell boundary.
    private DensityVolume CornerVolume(in DensityVolume volume)
    {
        var maxBlockX = volume.MinBlockX + volume.SizeX - 1;
        var maxBlockY = volume.MinBlockY + volume.SizeY - 1;
        var maxBlockZ = volume.MinBlockZ + volume.SizeZ - 1;

        var firstCellX = FloatMath.FloorDiv(volume.MinBlockX, cellSizeXz);
        var firstCellY = FloatMath.FloorDiv(volume.MinBlockY, cellSizeY);
        var firstCellZ = FloatMath.FloorDiv(volume.MinBlockZ, cellSizeXz);

        var cellsX = FloatMath.FloorDiv(maxBlockX, cellSizeXz) - firstCellX + 1;
        var cellsY = FloatMath.FloorDiv(maxBlockY, cellSizeY) - firstCellY + 1;
        var cellsZ = FloatMath.FloorDiv(maxBlockZ, cellSizeXz) - firstCellZ + 1;

        // A last cell the volume only touches at its first block needs no corners past it.
        var cornersX = FloatMath.FloorMod(maxBlockX, cellSizeXz) == 0 ? cellsX : cellsX + 1;
        var cornersY = FloatMath.FloorMod(maxBlockY, cellSizeY) == 0 ? cellsY : cellsY + 1;
        var cornersZ = FloatMath.FloorMod(maxBlockZ, cellSizeXz) == 0 ? cellsZ : cellsZ + 1;

        return new DensityVolume(
            cornersX,
            cornersY,
            cornersZ,
            firstCellX * cellSizeXz,
            firstCellY * cellSizeY,
            firstCellZ * cellSizeXz,
            cellSizeXz,
            cellSizeY,
            cellSizeXz);
    }

    // Samples the cell corners together, then fills each cell's intersection with the volume.
    private void SampleBlocks(Span<float> output, in DensityVolume volume)
    {
        var corners = this.CornerVolume(volume);
        var cellsX = (volume.MinBlockX + volume.SizeX - 1 - corners.MinBlockX) / cellSizeXz + 1;
        var cellsY = (volume.MinBlockY + volume.SizeY - 1 - corners.MinBlockY) / cellSizeY + 1;
        var cellsZ = (volume.MinBlockZ + volume.SizeZ - 1 - corners.MinBlockZ) / cellSizeXz + 1;

        using var cornerValues = DensityBuffer.Sample(input, corners);

        for (var cellZ = 0; cellZ < cellsZ; cellZ++)
        {
            for (var cellX = 0; cellX < cellsX; cellX++)
            {
                for (var cellY = 0; cellY < cellsY; cellY++)
                    this.FillCell(output, volume, cornerValues.Span, corners, cellX, cellY, cellZ);
            }
        }
    }

    // Interpolates the blocks of one cell that lie in the volume like vanilla's fillCell: along z, then x, then a
    // slope added up each column.
    private void FillCell(
        Span<float> output,
        in DensityVolume volume,
        ReadOnlySpan<float> cornerValues,
        in DensityVolume corners,
        int cellX,
        int cellY,
        int cellZ)
    {
        // A cell the volume only touches at its first block uses its near corners as its far ones.
        var nextX = Math.Min(cellX + 1, corners.SizeX - 1);
        var nextY = Math.Min(cellY + 1, corners.SizeY - 1);
        var nextZ = Math.Min(cellZ + 1, corners.SizeZ - 1);

        var x0y0z0 = cornerValues[corners.Index(cellX, cellY, cellZ)];
        var x1y0z0 = cornerValues[corners.Index(nextX, cellY, cellZ)];
        var x0y1z0 = cornerValues[corners.Index(cellX, nextY, cellZ)];
        var x1y1z0 = cornerValues[corners.Index(nextX, nextY, cellZ)];
        var x0y0z1 = cornerValues[corners.Index(cellX, cellY, nextZ)];
        var x1y0z1 = cornerValues[corners.Index(nextX, cellY, nextZ)];
        var x0y1z1 = cornerValues[corners.Index(cellX, nextY, nextZ)];
        var x1y1z1 = cornerValues[corners.Index(nextX, nextY, nextZ)];

        // The cell's first block relative to the volume's, and the cell's blocks that lie in the volume.
        var offsetX = corners.BlockX(cellX) - volume.MinBlockX;
        var offsetY = corners.BlockY(cellY) - volume.MinBlockY;
        var offsetZ = corners.BlockZ(cellZ) - volume.MinBlockZ;

        var startX = Math.Max(0, -offsetX);
        var startY = Math.Max(0, -offsetY);
        var startZ = Math.Max(0, -offsetZ);

        var endX = Math.Min(cellSizeXz, volume.SizeX - offsetX) - 1;
        var endY = Math.Min(cellSizeY, volume.SizeY - offsetY) - 1;
        var endZ = Math.Min(cellSizeXz, volume.SizeZ - offsetZ) - 1;

        for (var z = startZ; z <= endZ; z++)
        {
            var alphaZ = z * this.inverseCellSizeXz;
            var x0y0 = FloatMath.Lerp(alphaZ, x0y0z0, x0y0z1);
            var x0y1 = FloatMath.Lerp(alphaZ, x0y1z0, x0y1z1);
            var x1y0 = FloatMath.Lerp(alphaZ, x1y0z0, x1y0z1);
            var x1y1 = FloatMath.Lerp(alphaZ, x1y1z0, x1y1z1);

            for (var x = startX; x <= endX; x++)
            {
                var alphaX = x * this.inverseCellSizeXz;
                var bottom = FloatMath.Lerp(alphaX, x0y0, x1y0);
                var top = FloatMath.Lerp(alphaX, x0y1, x1y1);

                // The slope is added once per block, so its rounding accumulates up the column as in vanilla.
                var slope = (top - bottom) * this.inverseCellSizeY;
                var value = bottom + slope * startY;
                var index = volume.Index(offsetX + x, offsetY + startY, offsetZ + z);

                for (var y = startY; y <= endY; y++)
                {
                    output[index] = value;
                    index++;
                    value += slope;
                }
            }
        }
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

    public override bool IsFlat => fixedY is not null || input.IsFlat;

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
    // Both paths find one surface per column.
    public override bool IsFlat => true;

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
