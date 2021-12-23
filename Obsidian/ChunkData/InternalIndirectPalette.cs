using System.Runtime.CompilerServices;

namespace Obsidian.ChunkData;
internal sealed class InternalIndirectPalette<T> : BaseIndirectPalette<T>, IPalette<T> where T : struct
{
    public InternalIndirectPalette(byte bitCount) : base(bitCount)
    {
    }

    public override T GetValueFromIndex(int index)
    {
        if ((uint)index >= (uint)Count)
            ThrowHelper.ThrowOutOfRange();

        if (typeof(T).IsEnum)
        {
            return Unsafe.As<int, T>(ref Values[index]);
        }

        if (typeof(T) == typeof(Block))
        {
            var block = new Block(Values[index]);
            return Unsafe.As<Block, T>(ref block);
        }

        throw new NotSupportedException();
    }
}
