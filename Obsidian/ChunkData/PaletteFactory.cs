using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.ChunkData;

public static class PaletteFactory
{
    public static IPalette<IBlock> DetermineBlockPalette(byte bitsPerEntry)
    {
        return bitsPerEntry switch
        {
            0 => new SingleBlockValuePalette(),
            <= 3 and > 0 => new IndirectBlockPalette(4),
            >= 4 and <= 8 => new IndirectBlockPalette(bitsPerEntry),
            _ => new GlobalBlockStatePalette(BlocksRegistry.GlobalBitsPerBlocks)
        };
    }

    public static IPalette<BiomeCodec> DetermineBiomePalette(byte bitsPerEntry)
    {
        return bitsPerEntry switch
        {
            0 => new SingleBiomeValuePalette(),
            // Kept indirect past 3 bits too, so a biome is never too big for its storage when plugins add biomes;
            // BiomeContainer.WriteTo sends such a section with global ids, as vanilla's client reads it.
            _ => new IndirectBiomePalette(bitsPerEntry)
        };
    }
}
