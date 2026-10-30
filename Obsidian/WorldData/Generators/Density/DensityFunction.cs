using System.Buffers;

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
/// inlined, caches are transparent, and constant operands pick the same specialised arithmetic vanilla picks.
/// </para>
/// </remarks>
internal abstract class DensityFunction
{
    /// <summary>The value at a block position.</summary>
    public abstract float Sample(int x, int y, int z);

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
/// A pooled scratch buffer for one volume's values, returned to the pool when disposed.
/// </summary>
internal readonly struct DensityBuffer : IDisposable
{
    private readonly float[] array;
    private readonly int length;

    public DensityBuffer(int length)
    {
        this.array = ArrayPool<float>.Shared.Rent(length);
        this.length = length;
    }

    public Span<float> Span => this.array.AsSpan(0, this.length);

    /// <summary>Rents a buffer and fills it with <paramref name="function"/>'s values over the volume.</summary>
    public static DensityBuffer Sample(DensityFunction function, in DensityVolume volume)
    {
        var buffer = new DensityBuffer(volume.Size);
        function.SampleVolume(buffer.Span, volume);

        return buffer;
    }

    public void Dispose() => ArrayPool<float>.Shared.Return(this.array);
}
