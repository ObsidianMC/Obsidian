namespace Obsidian.Util
{
    public static class Extensions
    {
        //this is for ints
        public static int GetUnsignedRightShift(this int value, int s)
        {
            return (int)((uint)value) >> s;
        }

        //this is for longs

        public static long GetUnsignedRightShift(this long value, int s)
        {
            return (long)((ulong)value >> s);
        }
    }
}
