using System.Collections.Generic;

namespace Obsidian.Util
{
    public static class Extensions
    {
        //this is for ints
        public static int GetUnsignedRightShift(this int value, int s) => value >> s;

        //this is for longs

        public static long GetUnsignedRightShift(this long value, int s)
        {
            return (long)((ulong)value >> s);
        }

        public static string Capitalize(this string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        public static int GetVarIntLength(this int val)
        {
            int amount = 0;
            do
            {
                var temp = (sbyte)(val & 0b01111111);
                // Note: >>> means that the sign bit is shifted with the rest of the number rather than being left alone
                val >>= 7;
                if (val != 0)
                {
                    temp |= 127;
                }
                amount++;
            } while (val != 0);
            return amount;
        }
    }
}