using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.ChunkData;

public static class PaletteFactory
{
    public static IPalette<IBlock> DetermineBlockPalette(byte bitsPerEntry)
    {
        return bitsPerEntry switch
        {
            0 => new SingleBlockValuePalette(),
            > 4 and <= 8 => new IndirectBlockPalette(bitsPerEntry),
            _ => new GlobalBlockStatePalette(BlocksRegistry.GlobalBitsPerBlocks)
        };
    }

    public static IPalette<BiomeCodec> DetermineBiomePalette(byte bitsPerEntry)
    {
        return bitsPerEntry switch
        {
            0 => new SingleBiomeValuePalette(),
            > 0 and <= 3 => new IndirectBiomePalette(bitsPerEntry),
            _ => new GlobalBiomePalette(CodecRegistry.Biomes.GlobalBitsPerEntry)
        };
    }
}
