using Obsidian.Exceptions;

namespace Obsidian.ChunkData;

public sealed class IndirectPalette : BaseIndirectPalette<IBlock>, IPalette<IBlock>
{
    public IndirectPalette(byte bitCount) : base(bitCount)
    {
    }

    private IndirectPalette(int[] values, int bitCount, int count) : base(values, bitCount, count)
    {
    }

    public override IBlock? GetValueFromIndex(int index)
    {
        if ((uint)index >= (uint)Count)
            throw new MissingPaletteEntryException(index);

        return BlocksRegistry.Get(Values[index]);
    }

    public override IPalette<IBlock> Clone()
    {
        int[] valuesCopy = GC.AllocateUninitializedArray<int>(Values.Length);
        Array.Copy(Values, valuesCopy, Count);
        return new IndirectPalette(valuesCopy, BitCount, Count);
    }
}
