namespace Obsidian.ChunkData;

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

        return new GlobalBiomePalette();
    }
}
