using System.Threading;

namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// <c>cache</c>: <c>input</c>'s values, sampled once where vanilla's caches would sample them once.
/// </summary>
/// <remarks>
/// <para>
/// Density functions are pure, so remembering a value changes nothing as long as each sampling path remembers its own
/// (points and volumes can round differently). Each thread remembers the last point and the last volume it sampled:
/// a router samples the same function several times over the same cell corners, and a spline samples its
/// coordinates at the point its caller just sampled them.
/// </para>
/// <para>
/// A flat input (see <see cref="DensityFunction.IsFlat"/>) is sampled over one layer of a volume and copied down each
/// column, like vanilla's flat caches: the continents, erosion and ridges noises and the terrain splines built on
/// them don't change with y.
/// </para>
/// </remarks>
internal sealed class CacheFunction(DensityFunction input) : DensityFunction
{
    // Volumes larger than this, such as a chunk's every block, are sampled once by their callers already.
    private const int MaxRememberedVolume = 4096;

    private readonly bool flat = input.IsFlat;
    private readonly ThreadLocal<Remembered> remembered = new(() => new Remembered());

    public DensityFunction Input => input;

    public override bool IsFlat => this.flat;

    public override float Sample(int x, int y, int z)
    {
        var last = this.remembered.Value!;
        var pointY = this.flat ? 0 : y;

        if (last.HasPoint && last.PointX == x && last.PointY == pointY && last.PointZ == z)
            return last.PointValue;

        var value = input.Sample(x, y, z);
        last.RememberPoint(x, pointY, z, value);

        return value;
    }

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        if (!this.flat || volume.SizeY == 1)
        {
            this.SampleRemembered(output, volume);
            return;
        }

        var layer = volume with { SizeY = 1 };
        using var values = new DensityBuffer(layer.Size);
        this.SampleRemembered(values.Span, layer);

        for (var indexZ = 0; indexZ < volume.SizeZ; indexZ++)
        {
            for (var indexX = 0; indexX < volume.SizeX; indexX++)
            {
                var value = values.Span[layer.Index(indexX, 0, indexZ)];
                output.Slice(volume.Index(indexX, 0, indexZ), volume.SizeY).Fill(value);
            }
        }
    }

    private void SampleRemembered(Span<float> output, in DensityVolume volume)
    {
        if (volume.Size > MaxRememberedVolume)
        {
            input.SampleVolume(output, volume);
            return;
        }

        var last = this.remembered.Value!;

        if (last.HasVolume && last.Volume == volume)
        {
            last.Values.AsSpan(0, volume.Size).CopyTo(output);
            return;
        }

        input.SampleVolume(output, volume);
        last.RememberVolume(volume, output);
    }

    private sealed class Remembered
    {
        public bool HasPoint { get; private set; }

        public int PointX { get; private set; }

        public int PointY { get; private set; }

        public int PointZ { get; private set; }

        public float PointValue { get; private set; }

        public bool HasVolume { get; private set; }

        public DensityVolume Volume { get; private set; }

        public float[] Values { get; private set; } = [];

        public void RememberPoint(int x, int y, int z, float value)
        {
            this.HasPoint = true;
            this.PointX = x;
            this.PointY = y;
            this.PointZ = z;
            this.PointValue = value;
        }

        public void RememberVolume(in DensityVolume volume, ReadOnlySpan<float> values)
        {
            if (this.Values.Length < values.Length)
                this.Values = new float[MaxRememberedVolume];

            values.CopyTo(this.Values);
            this.Volume = volume;
            this.HasVolume = true;
        }
    }
}
