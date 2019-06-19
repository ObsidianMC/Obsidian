namespace Obsidian.Util
{
    public static class Extensions
    {
        public static int GetUnsignedRightShift(this int value, int s)
        {
            return (int)((uint)value) >> s;
        }

        public static long GetUnsignedRightShift(this long value, int s)
        {
            return (long)((ulong)value) >> s;
        }
    }
}
