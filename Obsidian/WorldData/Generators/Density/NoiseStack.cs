using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// A sum of gradient noise layers, each sampled at its own frequency and scaled by its own amplitude. 26.3's noises
/// (<see cref="NoiseParameters"/>) and <c>old_blended_noise</c> are stacks.
/// </summary>
/// <remarks>
/// Matches vanilla's NoiseStack: frequencies and coordinates are double, amplitudes and the running sum float, and the
/// layers add in order.
/// </remarks>
internal sealed class NoiseStack(IReadOnlyList<NoiseStack.Layer> layers)
{
    // An array, so that sampling enumerates the layers without allocating an enumerator.
    private readonly Layer[] layers = [.. layers];

    public readonly record struct Layer(PerlinNoise Noise, double Frequency, float Amplitude);

    public IReadOnlyList<Layer> Layers => this.layers;

    /// <summary>
    /// The noise at a point, as a density function samples one: each layer at the point times its frequency.
    /// </summary>
    public float Get(double x, double y, double z)
    {
        var value = 0.0f;

        foreach (var layer in this.layers)
        {
            var frequency = layer.Frequency;
            value += layer.Amplitude * layer.Noise.Get(x * frequency, y * frequency, z * frequency);
        }

        return value;
    }

    /// <summary>Samples selected volume positions with the volume path's scale-before-coordinate ordering.</summary>
    /// <remarks>
    /// Positions are decoded once, then each layer is added to every position in turn, which adds each position's
    /// layers in the same order as sampling it alone.
    /// </remarks>
    public void SampleSelected(
        Span<float> output,
        in DensityVolume volume,
        ReadOnlySpan<int> indices,
        double xzScale,
        double yScale)
    {
        var count = indices.Length;
        var positions = ArrayPool<int>.Shared.Rent(count * 3);

        try
        {
            var xs = positions.AsSpan(0, count);
            var ys = positions.AsSpan(count, count);
            var zs = positions.AsSpan(count * 2, count);

            for (var i = 0; i < count; i++)
                volume.Position(indices[i], out xs[i], out ys[i], out zs[i]);

            var values = output[..count];
            values.Clear();

            foreach (var layer in this.layers)
            {
                // Vanilla multiplies scale by frequency first over a volume; point sampling rounds differently.
                var layerXzScale = xzScale * layer.Frequency;
                var layerYScale = yScale * layer.Frequency;
                var amplitude = layer.Amplitude;
                var noise = layer.Noise;
                var i = 0;

                if (PerlinNoise.CanSampleEight)
                {
                    for (; i + 8 <= values.Length; i += 8)
                    {
                        var sampled = noise.Get8(
                            Scale(xs, i, layerXzScale, upper: false),
                            Scale(xs, i, layerXzScale, upper: true),
                            Scale(ys, i, layerYScale, upper: false),
                            Scale(ys, i, layerYScale, upper: true),
                            Scale(zs, i, layerXzScale, upper: false),
                            Scale(zs, i, layerXzScale, upper: true));
                        Accumulate(values, i, amplitude, sampled);
                    }
                }

                for (; i < values.Length; i++)
                    values[i] += amplitude * noise.Get(xs[i] * layerXzScale, ys[i] * layerYScale, zs[i] * layerXzScale);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(positions);
        }
    }

    /// <summary>
    /// Adds the noise times <paramref name="amplitude"/> to each point of a volume, as vanilla samples volumes: the
    /// scale is multiplied by each layer's frequency before the block coordinate is, which rounds differently from
    /// <see cref="Get"/>. Interpolated functions sample their cell corners this way.
    /// </summary>
    public void AddToVolume(Span<float> buffer, in DensityVolume volume, double xzScale, double yScale, float amplitude)
    {
        // The offsets of eight consecutive y from the first.
        var steps = Vector256.Create(0, 1, 2, 3, 4, 5, 6, 7) * volume.StepY;

        foreach (var layer in this.layers)
        {
            var layerXzScale = xzScale * layer.Frequency;
            var layerYScale = yScale * layer.Frequency;
            var layerAmplitude = amplitude * layer.Amplitude;
            var noise = layer.Noise;

            var index = 0;
            for (var indexZ = 0; indexZ < volume.SizeZ; indexZ++)
            {
                var z = volume.BlockZ(indexZ) * layerXzScale;

                for (var indexX = 0; indexX < volume.SizeX; indexX++)
                {
                    var x = volume.BlockX(indexX) * layerXzScale;
                    var indexY = 0;

                    // Eight y of the column at once.
                    if (PerlinNoise.CanSampleEight)
                    {
                        var xs = Vector256.Create(x);
                        var zs = Vector256.Create(z);

                        for (; indexY + 8 <= volume.SizeY; indexY += 8)
                        {
                            var blockYs = Vector256.Create(volume.BlockY(indexY)) + steps;
                            var sampled = noise.Get8(
                                xs,
                                xs,
                                Scale(blockYs.GetLower(), layerYScale),
                                Scale(blockYs.GetUpper(), layerYScale),
                                zs,
                                zs);
                            Accumulate(buffer, index, layerAmplitude, sampled);
                            index += 8;
                        }
                    }

                    for (; indexY < volume.SizeY; indexY++)
                    {
                        var y = volume.BlockY(indexY) * layerYScale;
                        buffer[index] += layerAmplitude * noise.Get(x, y, z);
                        index++;
                    }
                }
            }
        }
    }

    // Four ints from start (or the four after them), widened to double and times the scale, as the scalar path's
    // int times double.
    private static Vector256<double> Scale(ReadOnlySpan<int> values, int start, double scale, bool upper)
    {
        var ints = Vector128.Create<int>(values.Slice(upper ? start + 4 : start, 4));
        return Scale(ints, scale);
    }

    private static Vector256<double> Scale(Vector128<int> values, double scale) =>
        Avx.ConvertToVector256Double(values) * Vector256.Create(scale);

    // Adds the amplitude times eight samples to eight values from start, as the scalar path's value += amplitude * n.
    private static void Accumulate(Span<float> values, int start, float amplitude, Vector256<float> sampled)
    {
        ref var first = ref MemoryMarshal.GetReference(values.Slice(start, 8));
        var sum = Vector256.LoadUnsafe(ref first) + Vector256.Create(amplitude) * sampled;
        sum.StoreUnsafe(ref first);
    }
}
