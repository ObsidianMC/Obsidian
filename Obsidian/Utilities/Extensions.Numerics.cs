using System.Runtime.CompilerServices;

namespace Obsidian.Utilities;

public static partial class Extensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToChunkCoord(this int value) => value >> 4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (int x, int z) ToChunkCoord(this VectorF value) => ((int)value.X >> 4, (int)value.Z >> 4);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (int x, int z) ToChunkCoord(this Vector value) => (value.X >> 4, value.Z >> 4);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetUnsignedRightShift(this int value, int s) => value >> s;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetUnsignedRightShift(this long value, int s) => (long)((ulong)value >> s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float NextSingle(this Random random)
    {
        return (float)random.NextDouble();
    }

    public static int GetVarIntLength(this int value)
    {
        // Alternative implementation:
        // uint val = (uint)value;
        // int amount = 0;
        // do
        // {
        //     val >>= 7;
        //     amount++;
        // } while (val != 0);
        // return amount;

        // Note: Implementation below is just as fast for 1-byte numbers, but
        //       notably faster for more bytes, as there are no jumps and/or branches

        // | 1      Formula doesn't work with 0, so we have to set the value to at least 1
        // LZC      Count all leading zeroes, this gives us position of the MS bit with 1
        // - 38     Equivalent of - 31 - 7
        //  -31     We need the MSB index from the right instead from the left
        //  - 7     The result is always at least one byte (= 7 source bits)
        // * - 1171 >> 13 is an optimized version of / -7

        return (System.Numerics.BitOperations.LeadingZeroCount((uint)value | 1) - 38) * -1171 >> 13;
    }
}
