namespace Obsidian.ChunkData;

public sealed class IndirectPalette<T> : BaseIndirectPalette<T>, IPalette<T> where T : IPaletteValue<T>
{
    public IndirectPalette(byte bitCount) : base(bitCount)
    {
    }

    private IndirectPalette(int[] values, int bitCount, int count) : base(values, bitCount, count)
    {
    }

    public override T? GetValueFromIndex(int index)
    {
        if ((uint)index >= (uint)Count)
            ThrowHelper.ThrowOutOfRange();

        return T.Construct(Values[index]);
    }

    public override IPalette<T> Clone()
    {
        int[] valuesCopy = GC.AllocateUninitializedArray<int>(Values.Length);
        Array.Copy(Values, valuesCopy, Count);
        return new IndirectPalette<T>(valuesCopy, BitCount, Count);
    }
}
