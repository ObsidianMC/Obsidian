using System.Runtime.CompilerServices;

namespace Obsidian.Utilities
{
    public static class NumericsHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LongToInts(long l, out int a, out int b)
        {
            a = (int)(l & uint.MaxValue);
            b = (int)(l >> 32);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long IntsToLong(int a, int b) => ((long)b << 32) | (uint)a;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Modulo(int x, int mod) => (x % mod + mod) % mod;
    }
}
