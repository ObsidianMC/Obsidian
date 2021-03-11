using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.Util
{
    // This implements XorShift+.
    public class XorshiftRandom
    {
        private ulong state_a;
        private ulong state_b;
        private Semaphore semaphore;

        public XorshiftRandom()
        {
            state_a = (ulong)Environment.TickCount64;
            state_b = (ulong)Environment.TickCount64 >> 32;
            semaphore = new Semaphore(1, 1);
        }

        public int Next()
        {
            var value = step();
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
            return Convert.ToDouble(step());
        }

        public ulong NextUlong()
        {
            return step();
        }

        public float NextSingle()
        {
            return Convert.ToSingle(step());
        }

        /// <summary>
        /// Increases the RNG by one step and returns it's ulong value.
        /// https://en.wikipedia.org/wiki/Xorshift#xorshift+                                                                                           
        /// </summary>
        /// <returns></returns>
        private ulong step()
        {
            semaphore.WaitOne();
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
    }
}
