namespace Obsidian.ChunkData;

public static class Palette
{
    public static IPalette<IBlock> DetermineBlockPalette(this byte bitsPerEntry)
    {
        return bitsPerEntry switch
        {
            0 => new IndirectPalette(0),
            <= 4 => new IndirectPalette(4),
            > 4 and <= 8 => new IndirectPalette(bitsPerEntry),
            _ => new GlobalBlockStatePalette()
        };
    }

    public static IPalette<Biome> DetermineBiomePalette(this byte bitsPerEntry)
    {
        return bitsPerEntry <= 3
            ? new InternalIndirectPalette<Biome>(bitsPerEntry)
            : new GlobalBiomePalette();
    }
}
