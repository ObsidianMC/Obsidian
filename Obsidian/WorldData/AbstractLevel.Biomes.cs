using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    private long? biomeZoomSeed;

    /// <summary>Reads the seed-dependent block biome from loaded storage, shared by spawning and weather.</summary>
    internal BiomeCodec BiomeAt(IChunk fallback, int x, int y, int z)
    {
        var seed = this.biomeZoomSeed ??= BiomeManager.ObfuscateSeed(this.LevelData.RandomSeed);
        return MobSpawner.SpawnBiomeAt(seed, this.GetLoadedChunk, fallback, x, y, z);
    }
}
