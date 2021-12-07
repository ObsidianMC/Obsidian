using Obsidian.Net;

namespace Obsidian.ChunkData;
public interface IPalette
{
    public bool IsFull { get; }

    void WriteTo(MinecraftStream stream);

    Task WriteToAsync(MinecraftStream stream);

    Task ReadFromAsync(MinecraftStream stream);
}

public static class Palette
{
    public static IBlockStatePalette DetermineBlockPalette(this byte bitsPerEntry)
    {
        if (bitsPerEntry <= 4)
            return new IndirectBlockStatePalette(4);
        else if (bitsPerEntry > 4 || bitsPerEntry <= 8)
            return new IndirectBlockStatePalette(bitsPerEntry);

        return new GlobalBlockStatePalette();
    }

    public static IBiomePalette DetermineBiomePalette(this byte bitsPerEntry)
    {
        if (bitsPerEntry <= 3)
            return new IndirectBiomePalette(bitsPerEntry);

        throw new NotImplementedException("Implement global biome palette");
    }
}

