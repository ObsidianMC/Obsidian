using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// 2D simplex noise, which places the End's outer islands. Double precision, narrowed to float at the end like
/// vanilla's SimplexNoise.
/// </summary>
internal sealed class SimplexNoise(IRandomSource random) : GradientNoise(random, 0.0)
{
    private static readonly double sqrt3 = Math.Sqrt(3.0);
    private static readonly double f2 = 0.5 * (sqrt3 - 1.0);
    private static readonly double g2 = (3.0 - sqrt3) / 6.0;

    public float Get(double x, double y)
    {
        var offsetX = x + this.OffsetX;
        var offsetY = y + this.OffsetY;

        var skew = (offsetX + offsetY) * f2;
        var i = (int)Math.Floor(offsetX + skew);
        var j = (int)Math.Floor(offsetY + skew);

        var unskew = (i + j) * g2;
        var x0 = offsetX - (i - unskew);
        var y0 = offsetY - (j - unskew);

        var (i1, j1) = x0 > y0 ? (1, 0) : (0, 1);
        var x1 = x0 - i1 + g2;
        var y1 = y0 - j1 + g2;
        var x2 = x0 - 1.0 + 2.0 * g2;
        var y2 = y0 - 1.0 + 2.0 * g2;

        var ii = i & 0xFF;
        var jj = j & 0xFF;
        var gradient0 = this.Permute(ii + this.Permute(jj)) % 12;
        var gradient1 = this.Permute(ii + i1 + this.Permute(jj + j1)) % 12;
        var gradient2 = this.Permute(ii + 1 + this.Permute(jj + 1)) % 12;

        var n0 = Corner(gradient0, x0, y0);
        var n1 = Corner(gradient1, x1, y1);
        var n2 = Corner(gradient2, x2, y2);

        return (float)(70.0 * (n0 + n1 + n2));
    }

    // Vanilla's 3D corner contribution with z = 0, so the z term (0 * 0) keeps its place in the sums.
    private static double Corner(int index, double x, double y)
    {
        var t = 0.5 - x * x - y * y - 0.0 * 0.0;
        if (t < 0.0)
            return 0.0;

        t *= t;
        var gradient = gradients[index];
        var dot = gradient.X * x + gradient.Y * y + gradient.Z * 0.0;

        return t * t * dot;
    }
}

/// <summary>
/// <c>end_outer_islands</c>: the height offset of the End's outer islands at a column, from -0.84375 to 0.5625.
/// </summary>
/// <remarks>
/// Matches vanilla's EndIslandFunction: seeded from the world seed's legacy random after 17,292 draws, sampled per
/// 8-block section in float, without the central island (which the End's data adds with <c>distance_to_point</c>).
/// </remarks>
internal sealed class EndOuterIslandsFunction : DensityFunction
{
    private readonly SimplexNoise islandNoise;

    public EndOuterIslandsFunction(long seed)
    {
        var random = new LegacyRandomSource(seed);
        random.ConsumeCount(17292);

        this.islandNoise = new SimplexNoise(random);
    }

    public override bool IsFlat => true;

    public override float Sample(int x, int y, int z)
    {
        var height = this.HeightValue(x / 8, z / 8);
        return (height - 8.0f) / 128.0f;
    }

    private float HeightValue(int sectionX, int sectionZ)
    {
        var chunkX = sectionX / 2;
        var chunkZ = sectionZ / 2;
        var subSectionX = sectionX % 2;
        var subSectionZ = sectionZ % 2;
        var offset = -100.0f;

        for (var dx = -12; dx <= 12; dx++)
        {
            for (var dz = -12; dz <= 12; dz++)
            {
                long islandX = chunkX + dx;
                long islandZ = chunkZ + dz;

                var farEnough = islandX * islandX + islandZ * islandZ > 4096L;
                if (!farEnough)
                    continue;

                var isIsland = this.islandNoise.Get(islandX, islandZ) < -0.9f;
                if (!isIsland)
                    continue;

                var size = (MathF.Abs(islandX) * 3439.0f + MathF.Abs(islandZ) * 147.0f) % 13.0f + 9.0f;
                float distanceX = subSectionX - dx * 2;
                float distanceZ = subSectionZ - dz * 2;
                var distance = MathF.Sqrt(distanceX * distanceX + distanceZ * distanceZ);

                var islandOffset = FloatMath.Clamp(100.0f - distance * size, -100.0f, 80.0f);
                offset = MathF.Max(offset, islandOffset);
            }
        }

        return offset;
    }
}
