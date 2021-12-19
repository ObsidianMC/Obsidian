using Obsidian.Net;

namespace Obsidian.ChunkData;
public interface IPalette<T>
{
    public int[] Values { get; }

    public int Size { get; }

    public bool IsFull { get; }

    public int GetIdFromValue(T value);
    public T? GetValueFromIndex(int index);

    public void WriteTo(MinecraftStream stream);

    public Task WriteToAsync(MinecraftStream stream);

    public Task ReadFromAsync(MinecraftStream stream);
}

public static class Palette
{
    public static IPalette<Block> DetermineBlockPalette(this byte bitsPerEntry)
    {
        if (bitsPerEntry <= 4)
            return new IndirectPalette<Block>(4);
        else if (bitsPerEntry > 4 || bitsPerEntry <= 8)
            return new IndirectPalette<Block>(bitsPerEntry);

        return new GlobalBlockStatePalette();
    }

    public static IPalette<Biomes> DetermineBiomePalette(this byte bitsPerEntry)
    {
        if (bitsPerEntry <= 3)
            return new InternalIndirectPalette<Biomes>(bitsPerEntry);

        throw new NotImplementedException("Implement global biome palette");
    }
}

