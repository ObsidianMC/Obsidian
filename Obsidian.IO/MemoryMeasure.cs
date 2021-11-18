using System.Runtime.CompilerServices;
using System.Text;

namespace Obsidian.IO;

public static class MemoryMeasure
{
    private static readonly Encoding utf8 = Encoding.UTF8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe int GetByteCount<T>(T[] array) where T : unmanaged
    {
        return array.Length * sizeof(T);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe int GetByteCount<T>(IList<T> list) where T : unmanaged
    {
        return list.Count * sizeof(T);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe int GetByteCount<T>() where T : unmanaged
    {
        return sizeof(T);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetByteCount(string value)
    {
        return utf8.GetByteCount(value);
    }

    public static int GetVarIntByteCount(this int val)
    {
        int amount = 0;
        do
        {
            val >>= 7;
            amount++;
        } while (val != 0);

        return amount;
    }
}
