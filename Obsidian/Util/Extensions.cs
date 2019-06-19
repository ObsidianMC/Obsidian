using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.Util
{
    public static class Extensions
    {

        public static long GetUnsignedRightShift(this long value, int s)
        {
            return (long)((ulong)value >> s);
        }
    }
}
