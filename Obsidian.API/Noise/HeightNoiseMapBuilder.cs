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
public class HeightNoiseMapBuilder : NoiseMapBuilder
{
    public NoiseCube SourceNoiseCube { get; set; }

    public double BiasValue { get; set; } = 0;

    protected override void PrepareBuild()
    {
        if (destWidth <= 0 ||
            destHeight <= 0 ||
            SourceNoiseCube == null ||
            DestNoiseMap == null)
            throw new InvalidOperationException("Builder isn't properly set up.");

        DestNoiseMap.SetSize(destHeight, destWidth);
    }

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
                for (int y = yExtent; y > 0; y--)
                {
                    if (nc.GetValue((int)xCur, y, (int)zCur) > BiasValue)
                    {
                        finalValue = y;
                        break;
                    }
                }
                finalValue /= yExtent;
                finalValue *= 2.0f;
                finalValue -= 1.33f;
                DestNoiseMap[x, z] = finalValue;
            }
        });
    }
}
