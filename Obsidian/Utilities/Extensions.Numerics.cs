using Obsidian.API;
using System;
using System.Collections.Generic;
using System.Numerics;
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

        /// <summary>
        /// Determines whether the given type is unsigned.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns><c>True</c> if the type is unsigned; otherwise, <c>false</c>.</returns>
        public static bool IsUnsigned(this Type type)
        {
            switch (type)
            {
                case var _ when type == typeof(sbyte):
                case var _ when type == typeof(short):
                case var _ when type == typeof(int):
                case var _ when type == typeof(long):
                case var _ when type == typeof(float):
                case var _ when type == typeof(double):
                case var _ when type == typeof(decimal):
                case var _ when type == typeof(BigInteger):
                {
                    return false;
                }
                case var _ when type == typeof(byte):
                case var _ when type == typeof(ushort):
                case var _ when type == typeof(uint):
                case var _ when type == typeof(ulong):
                {
                    return true;
                }
            }

            throw new InvalidOperationException($"{nameof(type)} is not a numeric type.");
        }
    }
}
