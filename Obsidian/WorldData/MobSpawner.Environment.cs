using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData;

internal sealed partial class MobSpawner
{
    private long? biomeZoomSeed;

    private BiomeCodec SpawnBiomeAt(IChunk fallback, int x, int y, int z)
    {
        var seed = this.biomeZoomSeed ??= BiomeManager.ObfuscateSeed(level.LevelData.RandomSeed);
        return SpawnBiomeAt(seed, level.GetLoadedChunk, fallback, x, y, z);
    }

    /// <summary>
    /// Natural mob spawns request full-resolution biomes in 26.3. Probability during population uses the raw
    /// quart biome instead. Read stored sections, including adjacent chunks, without recursively loading terrain.
    /// </summary>
    internal static BiomeCodec SpawnBiomeAt(long zoomSeed, Func<int, int, IChunk?> loadedChunk,
        IChunk fallback, int x, int y, int z)
    {
        var quart = BiomeManager.QuartAtBlock(zoomSeed, x, y, z);
        var chunk = loadedChunk(quart.X >> 2, quart.Z >> 2);

        if (chunk is null)
        {
            // At the edge of loaded storage, retain the local biome until the neighboring chunk is available.
            return fallback.GetBiome(x, Math.Clamp(y, fallback.MinY, fallback.MinY + fallback.Height - 1), z);
        }

        var blockY = Math.Clamp(quart.Y << 2, chunk.MinY, chunk.MinY + chunk.Height - 1);
        return chunk.GetBiome(quart.X << 2, blockY, quart.Z << 2);
    }
}
