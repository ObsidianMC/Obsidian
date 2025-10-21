using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Registries;

namespace Obsidian.ChunkData;

public static class Palette
{
    public static IPalette<IBlock> DetermineBlockPalette(this byte bitsPerEntry)
    {
        return bitsPerEntry switch
        {
            <= 4 => new IndirectBlockPalette(4),
            > 4 and <= 8 => new IndirectBlockPalette(bitsPerEntry),
            _ => new GlobalBlockStatePalette(BlocksRegistry.GlobalBitsPerBlocks)
        };
    }

    public static IPalette<BiomeCodec> DetermineBiomePalette(this byte bitsPerEntry)
    {
        if (bitsPerEntry <= 3)
            return new IndirectBiomePalette(bitsPerEntry);

        return new GlobalBiomePalette(CodecRegistry.Biomes.GlobalBitsPerEntry);
    }
}
