using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.ChunkData;

public class GlobalBiomePalette : IPalette<BiomeCodec>
{
    public int[] Values => throw new NotSupportedException();
    public int BitCount { get; }
    public int Count => throw new NotSupportedException();
    public bool IsFull => false;

    public bool ShouldGrow => false;

    public GlobalBiomePalette(int bitCount)
    {
        this.BitCount = bitCount;
    }

    public bool TryGetId(BiomeCodec biome, out int id)
    {
        id = biome.Id;
        return true;
    }

    public int GetOrAddId(BiomeCodec biome) => biome.Id;

    public BiomeCodec? GetValueFromIndex(int index) => CodecRegistry.GetBiome(index);

    public IPalette<BiomeCodec> Clone() => this;

    public void WriteTo(INetStreamWriter writer)
    {
    }
}
