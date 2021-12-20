namespace Obsidian.ChunkData;

public sealed class IndirectPalette<T> : BaseIndirectPalette<T>, IPalette<T> where T : IPaletteValue<T>
{
    public IndirectPalette(byte bitCount) : base(bitCount)
    {
    }

    public override T? GetValueFromIndex(int index)
    {
        if ((uint)index >= (uint)Count)
            ThrowHelper.ThrowOutOfRange();

        return T.Construct(Values[index]);
    }
}
