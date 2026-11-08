namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// A 26.3 density function, bound to a world's seed: a float value per block position.
/// </summary>
/// <remarks>
/// <para>
/// Vanilla samples a function either at one point or over a <see cref="DensityVolume"/>, and the two paths don't
/// always round alike: a multiplication skips its right side when the left is zero only at a point, and noise scales
/// its coordinates in a different order over a volume. Interpolated functions sample their cell corners as a volume
/// even at a point, so both paths have to match vanilla's. Functions whose two paths agree keep the default
/// <see cref="SampleVolume"/>, which samples each point.
/// </para>
/// <para>
/// <see cref="DensityCompiler"/> builds the functions from JSON the way vanilla's compiler does: references are
/// inlined, caches only remember values (<see cref="CacheFunction"/>), and constant operands pick the same
/// specialised arithmetic vanilla picks.
/// </para>
/// </remarks>
internal abstract class DensityFunction
{
    /// <summary>The value at a block position.</summary>
    public abstract float Sample(int x, int y, int z);

    /// <summary>
    /// Whether the value is the same at every y of a column, at a point and over a volume, so that a
    /// <see cref="CacheFunction"/> can sample each column once. A function that can't tell says no.
    /// </summary>
    public virtual bool IsFlat => false;

    /// <summary>
    /// Whether the value at a y is the same in every column, at a point and over a volume, so that a volume can be
    /// sampled over one column and copied across (see <see cref="SampleColumn"/>). A function that can't tell says no.
    /// </summary>
    /// <remarks>
    /// Over a volume the value may still depend on the volume's y origin and step, as an interpolation's rounding does,
    /// but not on its x and z: the column keeps the volume's y extent.
    /// </remarks>
    public virtual bool DependsOnlyOnY => false;

    /// <summary>
    /// Whether sampling every point of a volume costs little more than sampling most of them: the function samples
    /// no noise, spline or cache per point, only constants, coordinates, interpolated cell corners and arithmetic on
    /// them. Branching functions sample such branches at all their points rather than select each branch's points.
    /// A function that can't tell says no.
    /// </summary>
    public virtual bool SamplesCheaply => false;

    /// <summary>
    /// What the function's values can be at a point, over a volume and at selected positions (see
    /// <see cref="ValueBounds"/>). Functions work it out from their inputs' bounds as they're built; one that can't
    /// tell reports <see cref="ValueBounds.Unknown"/>.
    /// </summary>
    public virtual ValueBounds Bounds => ValueBounds.Unknown;

    /// <summary>
    /// Samples sorted, distinct indices of the original volume into a compact output buffer.
    /// Selection never changes the volume's origin: vanilla's interpolation accumulates rounding from that origin.
    /// Functions without a selective implementation retain their full-volume arithmetic and gather its results.
    /// </summary>
    public virtual void SampleSelected(Span<float> output, in DensityVolume volume, ReadOnlySpan<int> indices)
    {
        using var values = DensityBuffer.Sample(this, volume);
        var source = values.Span;

        for (var i = 0; i < indices.Length; i++)
            output[i] = source[indices[i]];
    }

    /// <summary>Uses the full volume when no selection is supplied, otherwise its selected indices.</summary>
    public void SampleSelection(Span<float> output, in DensityVolume volume, ReadOnlySpan<int> indices)
    {
        if (indices.IsEmpty)
            this.SampleVolume(output, volume);
        else
            this.SampleSelected(output, volume, indices);
    }

    /// <summary>Samples selected positions for functions whose point and volume arithmetic agree.</summary>
    protected void SampleSelectedPoints(Span<float> output, in DensityVolume volume, ReadOnlySpan<int> indices)
    {
        for (var i = 0; i < indices.Length; i++)
        {
            volume.Position(indices[i], out var x, out var y, out var z);
            output[i] = this.Sample(x, y, z);
        }
    }

    /// <summary>
    /// Samples a flat function (see <see cref="IsFlat"/>) over the bottom layer of <paramref name="volume"/> and copies
    /// each column's value down it, which writes the values sampling every point would.
    /// </summary>
    /// <remarks>
    /// Vanilla samples an uncached flat function at every point of a volume. Data packs name such functions outside
    /// interpolation, like Terratonic's lava tunnels, whose noises would otherwise be sampled at each block of a chunk.
    /// </remarks>
    protected void SampleLayer(Span<float> output, in DensityVolume volume)
    {
        var layer = volume with { SizeY = 1 };
        using var values = DensityBuffer.Sample(this, layer);

        for (var indexZ = 0; indexZ < volume.SizeZ; indexZ++)
        {
            for (var indexX = 0; indexX < volume.SizeX; indexX++)
            {
                var value = values.Span[layer.Index(indexX, 0, indexZ)];
                output.Slice(volume.Index(indexX, 0, indexZ), volume.SizeY).Fill(value);
            }
        }
    }

    /// <summary>
    /// Whether <see cref="SampleColumn"/> should sample <paramref name="volume"/>: the function depends only on y (see
    /// <see cref="DependsOnlyOnY"/>) and the volume has more than one column.
    /// </summary>
    protected bool SamplesByColumn(in DensityVolume volume) =>
        this.DependsOnlyOnY && (volume.SizeX > 1 || volume.SizeZ > 1);

    /// <summary>
    /// Samples a function that depends only on y (see <see cref="DependsOnlyOnY"/>) over the first column of
    /// <paramref name="volume"/> and copies it to the others, which writes the values sampling every point would.
    /// </summary>
    /// <remarks>
    /// Vanilla samples such a function at every point of a volume. Data packs name some outside interpolation, like the
    /// spline over the height above the world's bottom in Terratonic's lava tunnels, which would otherwise be evaluated
    /// at each block of a chunk.
    /// </remarks>
    protected void SampleColumn(Span<float> output, in DensityVolume volume)
    {
        var column = volume with
        {
            SizeX = 1,
            SizeZ = 1
        };

        // The first column's indices are the same in both volumes, since y varies fastest.
        var first = output[..volume.SizeY];
        this.SampleVolume(first, column);

        for (var start = volume.SizeY; start < volume.Size; start += volume.SizeY)
            first.CopyTo(output.Slice(start, volume.SizeY));
    }

    /// <summary>Writes the value at each point of <paramref name="volume"/> to <paramref name="output"/>.</summary>
    public virtual void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        var index = 0;

        for (var indexZ = 0; indexZ < volume.SizeZ; indexZ++)
        {
            var z = volume.BlockZ(indexZ);

            for (var indexX = 0; indexX < volume.SizeX; indexX++)
            {
                var x = volume.BlockX(indexX);

                for (var indexY = 0; indexY < volume.SizeY; indexY++)
                {
                    output[index] = this.Sample(x, volume.BlockY(indexY), z);
                    index++;
                }
            }
        }
    }
}

/// <summary>
/// A scratch buffer for one volume's values, borrowed from the thread's <see cref="ScratchStack{T}"/> and given back
/// when disposed. Rent it in a <c>using</c> declaration.
/// </summary>
internal readonly ref struct DensityBuffer : IDisposable
{
    private readonly ScratchBuffer<float> buffer;

    public DensityBuffer(int length)
    {
        this.buffer = ScratchBuffer<float>.Rent(length);
    }

    public Span<float> Span => this.buffer.Span;

    /// <summary>Rents a buffer and fills it with <paramref name="function"/>'s values over the volume.</summary>
    public static DensityBuffer Sample(DensityFunction function, in DensityVolume volume)
    {
        var buffer = new DensityBuffer(volume.Size);

        try
        {
            function.SampleVolume(buffer.Span, volume);
        }
        catch
        {
            buffer.Dispose();
            throw;
        }

        return buffer;
    }

    public void Dispose() => this.buffer.Dispose();
}
