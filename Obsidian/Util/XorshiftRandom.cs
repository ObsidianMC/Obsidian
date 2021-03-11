using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Obsidian.Util
{
    // This implements XorShift+.
    public sealed class XorshiftRandom : IDisposable
    {
        private ulong state_a;
        private ulong state_b;
        private SemaphoreSlim semaphore;

        public XorshiftRandom()
        {
            state_a = (ulong)Environment.TickCount64;
            state_b = (ulong)Environment.TickCount64 >> 32;
            semaphore = new SemaphoreSlim(1, 1);
        }

        public int Next()
        {
            var value = Step();
            return (int)(value & 0xFFFFFFFF);
        }

        public int Next(int maxValue)
        {
            return Next() % maxValue;
        }

        public int Next(int minValue, int maxValue)
        {
            return (Next() & (maxValue - minValue)) + minValue;
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

        /// <summary>
        /// Increases the RNG by one step and returns it's ulong value.
        /// https://en.wikipedia.org/wiki/Xorshift#xorshift+                                                                                           
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong Step()
        {
            semaphore.Wait();
            try
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
            finally
            {
                semaphore.Release();
            }
        }

        public void Dispose()
        {
            semaphore.Dispose();
        }
    }
}
