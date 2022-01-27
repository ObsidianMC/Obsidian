using Obsidian.Net;

namespace Obsidian.ChunkData;

public class GlobalBiomePalette : IPalette<Biomes>
{
    public int[] Values => throw new NotSupportedException();
    public int BitCount { get; }
    public int Count => throw new NotSupportedException();
    public bool IsFull => false;

    public GlobalBiomePalette(int bitCount)
    {
        BitCount = bitCount;
    }

    public bool TryGetId(Biomes biome, out int id)
    {
        id = (int)biome;
        return true;
    }

    public int GetOrAddId(Biomes biome) => (int)biome;

    public Biomes GetValueFromIndex(int index) => (Biomes)index;

    public IPalette<Biomes> Clone() => this;

    public Task WriteToAsync(MinecraftStream stream) => Task.CompletedTask;
    public Task ReadFromAsync(MinecraftStream stream) => Task.CompletedTask;
    public void WriteTo(MinecraftStream stream)
    {
    }
}
