using Obsidian.Utilities.Registry;

namespace Obsidian.ChunkData;

public static class Palette
{
    public static IPalette<IBlock> DetermineBlockPalette(this byte bitsPerEntry)
    {
        if (bitsPerEntry <= 4)
            return new IndirectPalette(4);
        else if (bitsPerEntry > 4 || bitsPerEntry <= 8)
            return new IndirectPalette(bitsPerEntry);

        return new GlobalBlockStatePalette(Registry.GlobalBitsPerBlocks);
    }

    public static IPalette<Biomes> DetermineBiomePalette(this byte bitsPerEntry)
    {
        if (bitsPerEntry <= 3)
            return new InternalIndirectPalette<Biomes>(bitsPerEntry);

        return new GlobalBiomePalette(Registry.GlobalBitsPerBiomes);
    }
}
