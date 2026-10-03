using Obsidian.API.Noise;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Height field of the main End island and the outer islands.
/// </summary>
/// <remarks>
/// Registry instances use seed 0, like vanilla's codec. World generation creates a seeded copy with <see cref="WithSeed"/>.
/// </remarks>
[DensityFunction("minecraft:end_islands")]
public sealed class EndIslandsDensityFunction : IDensityFunction
{
    private const float IslandThreshold = -0.9f;

    public string Type => "minecraft:end_islands";

    public double MinValue => -0.84375;

    public double MaxValue => 0.5625;

    private SimplexNoise IslandNoise
    {
        get => field ??= CreateNoise(0L);
        init;
    }

    /// <summary>
    /// Returns a copy of this function seeded with the world seed.
    /// </summary>
    public static EndIslandsDensityFunction WithSeed(long seed) => new() { IslandNoise = CreateNoise(seed) };

    public double GetValue(double x, double y, double z) => (GetHeightValue(this.IslandNoise, (int)x / 8, (int)z / 8) - 8.0) / 128.0;

    private static SimplexNoise CreateNoise(long seed)
    {
        var random = new LegacyRandomSource(seed);
        random.ConsumeCount(17292);
        return new SimplexNoise(random);
    }

    private static float GetHeightValue(SimplexNoise noise, int x, int z)
    {
        var chunkX = x / 2;
        var chunkZ = z / 2;
        var offsetX = x % 2;
        var offsetZ = z % 2;

        var height = Math.Clamp(100.0f - MathF.Sqrt(x * x + z * z) * 8.0f, -100.0f, 80.0f);

        for (var dx = -12; dx <= 12; dx++)
        {
            for (var dz = -12; dz <= 12; dz++)
            {
                long islandX = chunkX + dx;
                long islandZ = chunkZ + dz;

                if (islandX * islandX + islandZ * islandZ <= 4096L || noise.GetValue(islandX, islandZ) >= IslandThreshold)
                    continue;

                var size = (Math.Abs((float)islandX) * 3439.0f + Math.Abs((float)islandZ) * 147.0f) % 13.0f + 9.0f;
                float distanceX = offsetX - dx * 2;
                float distanceZ = offsetZ - dz * 2;
                var islandHeight = Math.Clamp(100.0f - MathF.Sqrt(distanceX * distanceX + distanceZ * distanceZ) * size, -100.0f, 80.0f);

                height = Math.Max(height, islandHeight);
            }
        }

        return height;
    }
}
