using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Exceptions;

namespace Obsidian.ChunkData;

public sealed class IndirectBlockPalette : BaseIndirectPalette<IBlock>, IPalette<IBlock>
{
    public IndirectBlockPalette(byte bitCount) : base(bitCount)
    {
    }

    private IndirectBlockPalette(int[] values, int bitCount, int count) : base(values, bitCount, count)
    {
    }

    public override IBlock? GetValueFromIndex(int index)
    {
        if ((uint)index >= (uint)Count)
            throw new MissingPaletteEntryException(index);

        var block = BlocksRegistry.Get(Values[index]);

        return block is null ? throw new MissingPaletteEntryException(index) : block;
    }

    public override IPalette<IBlock> Clone()
    {
        int[] valuesCopy = GC.AllocateUninitializedArray<int>(Values.Length);
        Array.Copy(Values, valuesCopy, Count);
        return new IndirectBlockPalette(valuesCopy, BitCount, Count);
    }

    protected override int GetValueId(IBlock value) => value.GetHashCode();
}


public sealed class IndirectBiomePalette : BaseIndirectPalette<BiomeCodec>, IPalette<BiomeCodec>
{
    public IndirectBiomePalette(byte bitCount) : base(bitCount)
    {
    }

    private IndirectBiomePalette(int[] values, int bitCount, int count) : base(values, bitCount, count)
    {
    }

    public override BiomeCodec? GetValueFromIndex(int index)
    {
        if ((uint)index >= (uint)Count)
            throw new MissingPaletteEntryException(index);

        var biome = CodecRegistry.GetBiome(Values[index]);

        return biome is null ? throw new MissingPaletteEntryException(index) : biome;
    }

    public override IPalette<BiomeCodec> Clone()
    {
        int[] valuesCopy = GC.AllocateUninitializedArray<int>(Values.Length);
        Array.Copy(Values, valuesCopy, Count);
        return new IndirectBiomePalette(valuesCopy, BitCount, Count);
    }

    protected override int GetValueId(BiomeCodec value) => value.Id;
}
