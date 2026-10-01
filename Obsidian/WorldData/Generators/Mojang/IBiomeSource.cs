using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Chooses the biome stored for each 4x4x4 block cell.
/// </summary>
public interface IBiomeSource
{
    /// <summary>
    /// Gets the biome at quart coordinates (block coordinates divided by 4).
    /// </summary>
    public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ);
}
