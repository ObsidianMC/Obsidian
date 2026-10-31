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
    public void SampleSelected(
        Span<float> output,
        in DensityVolume volume,
        ReadOnlySpan<int> indices,
        double xzScale,
        double yScale)
    {
        for (var i = 0; i < indices.Length; i++)
        {
            volume.Position(indices[i], out var x, out var y, out var z);
            var value = 0.0f;

            foreach (var layer in this.layers)
            {
                // Vanilla multiplies scale by frequency first over a volume; point sampling rounds differently.
                var layerXzScale = xzScale * layer.Frequency;
                var layerYScale = yScale * layer.Frequency;
                value += layer.Amplitude * layer.Noise.Get(x * layerXzScale, y * layerYScale, z * layerXzScale);
            }

            output[i] = value;
        }
    }

    /// <summary>
    /// Adds the noise times <paramref name="amplitude"/> to each point of a volume, as vanilla samples volumes: the
    /// scale is multiplied by each layer's frequency before the block coordinate is, which rounds differently from
    /// <see cref="Get"/>. Interpolated functions sample their cell corners this way.
    /// </summary>
    public void AddToVolume(Span<float> buffer, in DensityVolume volume, double xzScale, double yScale, float amplitude)
    {
        foreach (var layer in this.layers)
        {
            var layerXzScale = xzScale * layer.Frequency;
            var layerYScale = yScale * layer.Frequency;
            var layerAmplitude = amplitude * layer.Amplitude;

            var index = 0;
            for (var indexZ = 0; indexZ < volume.SizeZ; indexZ++)
            {
                var z = volume.BlockZ(indexZ) * layerXzScale;

                for (var indexX = 0; indexX < volume.SizeX; indexX++)
                {
                    var x = volume.BlockX(indexX) * layerXzScale;

                    for (var indexY = 0; indexY < volume.SizeY; indexY++)
                    {
                        var y = volume.BlockY(indexY) * layerYScale;
                        buffer[index] += layerAmplitude * layer.Noise.Get(x, y, z);
                        index++;
                    }
                }
            }
        }
    }
}
