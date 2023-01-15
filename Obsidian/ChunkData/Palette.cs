using Obsidian.Registries;

namespace Obsidian.ChunkData;

public static class Palette
{
    public static IPalette<IBlock> DetermineBlockPalette(this byte bitsPerEntry)
    {
        if (bitsPerEntry <= 4)
            return new IndirectPalette(4);
        else if (bitsPerEntry > 4 && bitsPerEntry <= 8)
            return new IndirectPalette(bitsPerEntry);

        return new GlobalBlockStatePalette(BlocksRegistry.GlobalBitsPerBlocks);
    }

    public static IPalette<Biome> DetermineBiomePalette(this byte bitsPerEntry)
    {
        if (bitsPerEntry <= 3)
            return new InternalIndirectPalette<Biome>(bitsPerEntry);

        return new GlobalBiomePalette(CodecRegistry.Biomes.GlobalBitsPerEntry);
    }
}
