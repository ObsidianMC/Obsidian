using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Obsidian.Utilities;

public static class GuidHelper
{
    private static HashAlgorithm hashAlgorithm;

    public static Guid FromStringHash(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var i128 = new Int128();

        hashAlgorithm ??= MD5.Create();

        hashAlgorithm.TryComputeHash(Encoding.UTF8.GetBytes(text), i128.AsSpan(), out _);

        i128.version = (byte)((i128.version & 0x0f) | 0x30);
        i128.variant = (byte)((i128.variant & 0x3f) | 0x80);

        return Unsafe.As<Int128, Guid>(ref i128);
    }

    public static int GetVersion(Guid guid)
    {
        ref Int128 i128 = ref Unsafe.As<Guid, Int128>(ref guid);
        return i128.version >> 4;
    }

    public static int GetVariant(Guid guid)
    {
        ref Int128 i128 = ref Unsafe.As<Guid, Int128>(ref guid);
        return (i128.variant >> 4) switch
        {
            <= 0b0111 => 0,
            <= 1011 => 1,
            <= 1101 => 2,
            1110 => 3,
            _ => -1
        };
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct Int128
    {
        [FieldOffset(0)]
        public int a;
        [FieldOffset(4)]
        public int b;
        [FieldOffset(8)]
        public int c;
        [FieldOffset(12)]
        public int d;

        [FieldOffset(0)]
        private byte start;

        [FieldOffset(7)]
        public byte version;
        [FieldOffset(8)]
        public byte variant;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> AsSpan()
        {
            return MemoryMarshal.CreateSpan(ref start, 16);
        }
    }
}
