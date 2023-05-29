using Obsidian.Registries;

namespace Obsidian.ChunkData;

public static class Palette
{
    public static IPalette<IBlock> DetermineBlockPalette(this byte bitsPerEntry)
    {
        return bitsPerEntry switch
        {
            <= 4 => new IndirectPalette(4),
            > 4 and <= 8 => new IndirectPalette(bitsPerEntry),
            _ => new GlobalBlockStatePalette(BlocksRegistry.GlobalBitsPerBlocks)
        };
    }

    public static IPalette<Biome> DetermineBiomePalette(this byte bitsPerEntry)
    {
        if (bitsPerEntry <= 3)
            return new InternalIndirectPalette<Biome>(bitsPerEntry);

        return new GlobalBiomePalette(CodecRegistry.Biomes.GlobalBitsPerEntry);
    }
}
