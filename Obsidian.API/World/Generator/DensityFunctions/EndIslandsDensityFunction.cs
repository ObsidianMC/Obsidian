using Obsidian.API.Noise;

namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:end_islands")]
public sealed class EndIslandsDensityFunction : IDensityFunction
{
    private const float IslandThreshold = -0.9F;

    public string Type => "minecraft:end_islands";

    // Vanilla's codec is a unit codec over seed 0, so nothing in the JSON ever sets this.
    public int Seed { get; init; }

    public double MinValue => -0.84375;

    public double MaxValue => 0.5625;

    // Vanilla seeds this from a LegacyRandomSource advanced by 17292 draws. We have no Java-compatible
    // LCG, so island placement won't line up with a vanilla world of the same seed - the shape, scale
    // and value range do. Every other noise in the project has the same caveat.
    private SimplexNoise islandNoise;

    private SimplexNoise IslandNoise => islandNoise ??= new SimplexNoise(new Random(Seed));

    public double GetValue(double x, double y, double z) =>
        (GetHeightValue(IslandNoise, (int)x / 8, (int)z / 8) - 8.0) / 128.0;

    private static float GetHeightValue(SimplexNoise islandNoise, int sectionX, int sectionZ)
    {
        int chunkX = sectionX / 2;
        int chunkZ = sectionZ / 2;
        int subSectionX = sectionX % 2;
        int subSectionZ = sectionZ % 2;

        float doffs = 100.0F - MathF.Sqrt(sectionX * sectionX + sectionZ * sectionZ) * 8.0F;
        doffs = Math.Clamp(doffs, -100.0F, 80.0F);

        for (int xo = -12; xo <= 12; xo++)
        {
            for (int zo = -12; zo <= 12; zo++)
            {
                long totalChunkX = chunkX + xo;
                long totalChunkZ = chunkZ + zo;

                if (totalChunkX * totalChunkX + totalChunkZ * totalChunkZ <= 4096L)
                    continue;

                if (islandNoise.GetValue(totalChunkX, totalChunkZ) >= IslandThreshold)
                    continue;

                float islandSize = (MathF.Abs(totalChunkX) * 3439.0F + MathF.Abs(totalChunkZ) * 147.0F) % 13.0F + 9.0F;
                float xd = subSectionX - xo * 2;
                float zd = subSectionZ - zo * 2;

                float newDoffs = 100.0F - MathF.Sqrt(xd * xd + zd * zd) * islandSize;
                newDoffs = Math.Clamp(newDoffs, -100.0F, 80.0F);

                doffs = Math.Max(doffs, newDoffs);
            }
        }

        return doffs;
    }
}
