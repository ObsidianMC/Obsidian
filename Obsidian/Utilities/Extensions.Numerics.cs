using Obsidian.API;
using System;
using System.Runtime.CompilerServices;

namespace Obsidian.Utilities
{
    public static partial class Extensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToChunkCoord(this int value) => value >> 4;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (int x, int z) ToChunkCoord(this VectorF value) => ((int)value.X >> 4, (int)value.Z >> 4);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetUnsignedRightShift(this int value, int s) => value >> s;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long GetUnsignedRightShift(this long value, int s) => (long)((ulong)value >> s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float NextSingle(this Random random)
        {
            return (float)random.NextDouble();
        }

        public static int GetVarIntLength(this int val)
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
}
