using System.Runtime.CompilerServices;

namespace Obsidian.ChunkData;
internal sealed class InternalIndirectPalette<T> : BaseIndirectPalette<T>, IPalette<T> where T : struct
{
    public InternalIndirectPalette(byte bitCount) : base(bitCount)
    {
    }

    private InternalIndirectPalette(int[] values, int bitCount, int count) : base(values, bitCount, count)
    {
    }

    public override T GetValueFromIndex(int index)
    {
        if ((uint)index >= (uint)Count)
            ThrowHelper.ThrowOutOfRange();

        if (typeof(T).IsEnum)
            return Unsafe.As<int, T>(ref Values[index]);

        throw new NotSupportedException();
    }

    public override IPalette<T> Clone()
    {
        int[] valuesCopy = GC.AllocateUninitializedArray<int>(Values.Length);
        Array.Copy(Values, valuesCopy, Count);
        return new InternalIndirectPalette<T>(valuesCopy, BitCount, Count);
    }
}
