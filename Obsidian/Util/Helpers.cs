namespace Obsidian.Util
{
    public static class Helpers
    {
        public static void LongToInts(long l, out int a, out int b)
        {
            a = (int)(l & uint.MaxValue);
            b = (int)(l >> 32);
        }

        public static long IntsToLong(int a, int b) => ((long)b << 32) | (uint)a;
    }
}
