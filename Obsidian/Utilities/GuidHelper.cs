using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Obsidian.Utilities
{
    public static class GuidHelper
    {
        private static HashAlgorithm hashAlgorithm;

        public static Guid FromStringHash(string text)
        {
            if (text is null)
                throw new ArgumentNullException(nameof(text));

            int hash = text.GetHashCode();

            var i128 = new Int128
            {
                a = HashCode.Combine(hash, 0),
                b = HashCode.Combine(hash, 256),
                c = HashCode.Combine(hash, 65536),
                d = HashCode.Combine(hash, 16777216)
            };

            return Unsafe.As<Int128, Guid>(ref i128);
        }

        public static Guid FromStringHashCryptographic(string text)
        {
            if (text is null)
                throw new ArgumentNullException(nameof(text));

            var i128 = new Int128();

            hashAlgorithm ??= MD5.Create();

            hashAlgorithm.TryComputeHash(text.AsByteSpan(), i128.AsSpan(), out _);

            return Unsafe.As<Int128, Guid>(ref i128);
        }

        private static ReadOnlySpan<byte> AsByteSpan(this string text)
        {
            ref char charPtr = ref Unsafe.AsRef(text.GetPinnableReference());
            ref byte ptr = ref Unsafe.As<char, byte>(ref charPtr);
            return MemoryMarshal.CreateReadOnlySpan(ref ptr, text.Length * 2);
        }

        [StructLayout(LayoutKind.Explicit)]
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

            public Span<byte> AsSpan()
            {
                return MemoryMarshal.CreateSpan(ref start, 16);
            }
        }
    }
}
