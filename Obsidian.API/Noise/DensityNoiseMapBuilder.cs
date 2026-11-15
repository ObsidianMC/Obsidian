using SharpNoise;
using SharpNoise.Builders;
using System.Threading;

namespace Obsidian.API.Noise;

/// <summary>
/// Builds a density noise map.
/// </summary>
/// <remarks>
/// This class builds a noise map by filling it with coherent-noise values
/// generated from the Y density of a noise cube.
/// </remarks>
public class DensityNoiseMapBuilder : NoiseMapBuilder
{
    /// <summary>
    /// Gets or sets the three-dimensional noise cube sampled to build the destination map.
    /// </summary>
    public NoiseCube SourceNoiseCube { get; set; }

    /// <summary>
    /// Validates positive destination dimensions and assigned source and destination, then sizes the map.
    /// </summary>
    protected override void PrepareBuild()
    {
        if (destWidth <= 0 ||
            destHeight <= 0 ||
            SourceNoiseCube == null ||
            DestNoiseMap == null)
        {
            throw new InvalidOperationException("Builder isn't properly set up.");
        }

        DestNoiseMap.SetSize(destHeight, destWidth);
    }

    /// <summary>
    /// Fills columns in parallel with the mean of their Y samples, honoring cancellation.
    /// </summary>
    protected override void BuildImpl(CancellationToken cancellationToken)
    {
        NoiseCube nc = SourceNoiseCube;

        var xExtent = nc.Width;
        var yExtent = nc.Height;
        var zExtent = nc.Depth;
        var xDelta = xExtent / destWidth;
        var zDelta = zExtent / destHeight;

        var po = new ParallelOptions()
        {
            CancellationToken = cancellationToken,
        };

        Parallel.For(0, destHeight, po, z =>
        {
            double zCur = z * zDelta;

            int x;
            double xCur;

            for (x = 0, xCur = 0; x < destWidth; x++, xCur += xDelta)
            {
                float finalValue = 0;
                for (int y = 0; y < yExtent; y++)
                {
                    finalValue += (float)nc.GetValue((int)xCur, y, (int)zCur);
                }
                finalValue /= yExtent;
                DestNoiseMap[x, z] = finalValue;
            }
        });
    }
}
