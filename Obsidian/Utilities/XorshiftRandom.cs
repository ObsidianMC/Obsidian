using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Obsidian.Util
{
    // This implements XorShift+.
    public struct XorshiftRandom
    {
        private ulong state_a;
        private ulong state_b;

        public XorshiftRandom(long seed)
        {
            state_a = (ulong)seed;
            state_b = (ulong)seed >> 32;
        }

        public int Next()
        {
            var value = Step();
            return (int)(value & 0xFFFFFFFF);
        }

        public int Next(int maxValue)
        {
            if (maxValue > 1)
            {
                int bits = Log2Ceiling((uint)maxValue);
                while (true)
                {
                    uint result = NextUInt32() >> (sizeof(uint) * 8 - bits);
                    if (result < (uint)maxValue)
                    {
                        return (int)result;
                    }
                }
            }

            return 0;
        }

        public int Next(int minValue, int maxValue)
        {
            uint exclusiveRange = (uint)(maxValue - minValue);

            if (exclusiveRange > 1)
            {
                int bits = Log2Ceiling(exclusiveRange);

                while (true)
                {
                    uint result = NextUInt32() >> (sizeof(uint) * 8 - bits);
                    if (result < exclusiveRange)
                    {
                        return (int)result + minValue;
                    }
                }
            }

            return minValue;
        }

        public double NextDouble()
        {
            return Convert.ToDouble(Step());
        }

        public ulong NextUlong()
        {
            return Step();
        }

        public float NextSingle()
        {
            return Convert.ToSingle(Step());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint NextUInt32()
        {
            ulong value = Step();
            return (uint)(value & 0xFFFFFFFF);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static XorshiftRandom Create()
        {
	        return new(Environment.TickCount64);
        }

        /// <summary>
        /// Increases the RNG by one step and returns it's ulong value.
        /// https://en.wikipedia.org/wiki/Xorshift#xorshift+                                                                                           
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong Step()
        {
            ulong t = state_a;
            ulong s = state_b;

            state_a = s;
            t ^= t << 23;
            t ^= t >> 17;
            t ^= s ^ (s >> 26);
            state_b = t;
            return t + s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Log2Ceiling(uint value)
        {
            int result = BitOperations.Log2(value);
            if (BitOperations.PopCount(value) != 1)
            {
                result++;
            }
            return result;
        }
    }
}
