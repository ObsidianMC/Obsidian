using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Obsidian.IO
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct BigEndianGuid
    {
        private readonly int _a;
        private readonly short _b;
        private readonly short _c;
        private readonly long _d;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(ref byte target, ref Guid guid)
        {
            ref BigEndianGuid data = ref Unsafe.As<Guid, BigEndianGuid>(ref guid);

            Unsafe.WriteUnaligned(ref target, BinaryPrimitives.ReverseEndianness(data._a));
            target = ref Unsafe.Add(ref target, 4);
            Unsafe.WriteUnaligned(ref target, BinaryPrimitives.ReverseEndianness(data._b));
            target = ref Unsafe.Add(ref target, 2);
            Unsafe.WriteUnaligned(ref target, BinaryPrimitives.ReverseEndianness(data._c));
            target = ref Unsafe.Add(ref target, 2);
            Unsafe.WriteUnaligned(ref target, data._d);
        }
    }
}
