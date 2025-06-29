using Obsidian.Nbt.Utilities;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Obsidian.Nbt;

// Encoding specification: https://web.archive.org/web/20211117120323/https://docs.oracle.com/javase/8/docs/api/java/io/DataInput.html
// +-----------------------------+-------------------------------------------------------------------+
// | Character range             | Bit values                                                        |
// +-----------------------------+-------------------------------------------------------------------+
// | \u0001 to \u007F            | 0 |             bits 6-0   |......................................|
// | \u0080 to \u07FF and \u0000 | 1 | 1 | 0 |     bits 10-6  | 1 | 0 | bits 5-0  |..................|
// | \u0800 to \uFFFF            | 1 | 1 | 1 | 0 | bits 15-12 | 1 | 0 | bits 11-6 | 1 | 0 | bits 5-0 |
// +-----------------------------+-------------------------------------------------------------------+

/// <summary>
/// Provides methods for working with a modification of UTF-8 encoding used by the NBT format.
/// </summary>
public static class ModifiedUtf8
{
    /// <summary>
    /// Encodes a span of characters into an array of bytes.
    /// </summary>
    /// <param name="chars">The span of characters to encode.</param>
    /// <param name="bytes">An array of bytes containing the results of encoding the specified sequence of characters.</param>
    /// <returns><c>true</c> if encoding the characters was successful; otherwise <c>false</c></returns>
    public static bool TryGetBytes(ReadOnlySpan<char> chars, [NotNullWhen(true)] out byte[]? bytes)
    {
        if (!TryGetByteCount(chars, out int byteCount))
        {
            bytes = null;
            return false;
        }

        if (byteCount == 0)
        {
            bytes = [];
            return true;
        }

        bytes = GC.AllocateUninitializedArray<byte>(byteCount);
        GetBytesCommon(chars, bytes);
        return true;
    }

    /// <summary>
    /// Encodes a span of characters into a sequence of bytes obtained from a buffer writer.
    /// </summary>
    /// <param name="chars">The span of characters to encode.</param>
    /// <param name="bufferWriter">Bytes output sink.</param>
    /// <returns><c>true</c> if encoding the characters was successful; otherwise <c>false</c></returns>
    public static bool TryGetBytes(ReadOnlySpan<char> chars, IBufferWriter<byte> bufferWriter)
    {
        if (!TryGetByteCount(chars, out int byteCount))
            return false;

        Span<byte> sink = bufferWriter.GetSpan(byteCount);
        if (sink.Length < byteCount)
            ThrowHelper.ThrowException_InsufficientBufferSize();

        GetBytesCommon(chars, sink);
        bufferWriter.Advance(byteCount);
        return true;
    }

    internal static void GetBytesCommon(ReadOnlySpan<char> chars, Span<byte> bytes)
    {
        if (chars.Length == bytes.Length)
        {
            if (BitConverter.IsLittleEndian && Sse2.IsSupported && bytes.Length >= 32)
            {
                GetBytesAsciiSse2(chars, bytes);
            }
            else
            {
                GetBytesAsciiScalar(chars, bytes);
            }
        }
        else
        {
            GetBytesScalar(chars, bytes);
        }
    }

    private static void GetBytesScalar(ReadOnlySpan<char> chars, Span<byte> bytes)
    {
        ref byte destination = ref MemoryMarshal.GetReference(bytes);
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (c < 0x80 && c != 0)
            {
                destination = (byte)c;
            }
            else if (c >= 0x800)
            {
                destination = (byte)(0xE0 | ((c >> 12) & 0x0F));
                destination = ref Unsafe.Add(ref destination, 1);
                destination = (byte)(0x80 | ((c >> 6) & 0x3F));
                destination = ref Unsafe.Add(ref destination, 1);
                destination = (byte)(0x80 | ((c >> 0) & 0x3F));
            }
            else
            {
                destination = (byte)(0xC0 | ((c >> 6) & 0x1F));
                destination = ref Unsafe.Add(ref destination, 1);
                destination = (byte)(0x80 | ((c >> 0) & 0x3F));
            }
            destination = ref Unsafe.Add(ref destination, 1);
        }
    }

    private static void GetBytesAsciiSse2(ReadOnlySpan<char> chars, Span<byte> bytes)
    {
        Debug.Assert(BitConverter.IsLittleEndian);
        Debug.Assert(Sse2.IsSupported);

        ref byte destination = ref MemoryMarshal.GetReference(bytes);
        ref byte source = ref Unsafe.As<char, byte>(ref MemoryMarshal.GetReference(chars));
        ref byte sourceEnd = ref Unsafe.Add(ref source, chars.Length * sizeof(char) - 31);

        while (Unsafe.IsAddressLessThan(ref source, ref sourceEnd))
        {
            Vector128<short> first = Unsafe.As<byte, Vector128<short>>(ref source);
            source = ref Unsafe.Add(ref source, 16);
            Vector128<short> second = Unsafe.As<byte, Vector128<short>>(ref source);
            source = ref Unsafe.Add(ref source, 16);

            Vector128<byte> packed = Sse2.PackUnsignedSaturate(first, second);

            Unsafe.WriteUnaligned(ref destination, packed);
            destination = ref Unsafe.Add(ref destination, 16);
        }

        sourceEnd = ref Unsafe.Add(ref sourceEnd, 31);
        while (Unsafe.IsAddressLessThan(ref source, ref sourceEnd))
        {
            destination = source;

            destination = ref Unsafe.Add(ref destination, 1);
            source = ref Unsafe.Add(ref source, 2);
        }
    }

    private static void GetBytesAsciiScalar(ReadOnlySpan<char> chars, Span<byte> bytes)
    {
        ref byte destination = ref MemoryMarshal.GetReference(bytes);
        for (int i = 0; i < chars.Length; i++)
        {
            destination = (byte)chars[i];
            destination = ref Unsafe.Add(ref destination, 1);
        }
    }

    /// <summary>
    /// Decodes a span of bytes into a string.
    /// </summary>
    /// <param name="bytes">The span of bytes to decode.</param>
    /// <returns>A <see cref="string"/> containing the results of decoding the specified sequence of bytes.</returns>
    /// <exception cref="FormatException">Sequence contained incorrectly formatted bytes.</exception>
    public static string GetString(ReadOnlySpan<byte> bytes)
    {
        if (TryGetString(bytes, out string? @string))
        {
            return @string;
        }

        throw new FormatException("Input data contained invalid bytes.");
    }

    /// <summary>
    /// Decodes a span of bytes into a string and returns a value indicating whether the conversion was successfull.
    /// </summary>
    /// <param name="bytes">The span of bytes to decode.</param>
    /// <param name="string">A <see cref="string"/> containing the results of decoding the specified sequence of bytes.</param>
    /// <returns><c>true</c> if decoding the bytes was successful; otherwise <c>false</c></returns>
    public static bool TryGetString(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out string? @string)
    {
        if (bytes.IsEmpty)
        {
            @string = string.Empty;
            return true;
        }

        if (TryGetCharCount(bytes, out int length))
        {
            @string = new string('\0', length);
            ref char stringRef = ref Unsafe.AsRef(in @string.GetPinnableReference());
            GetStringCommon(bytes, length, ref stringRef);
            return true;
        }

        @string = null;
        return false;
    }

    private static bool TryGetCharCount(ReadOnlySpan<byte> bytes, out int charCount)
    {
        Debug.Assert(!bytes.IsEmpty);

        charCount = 0;

        if (bytes.Length > ushort.MaxValue)
        {
            return false;
        }

        // Make sure that the last byte(s) is not partial
        int last = bytes[^1] >> 6;
        if (last > 2) // First byte of a byte group
        {
            return false;
        }
        else if (last == 2) // Part of a byte group
        {
            if (bytes.Length < 2)
                return false;

            last = bytes[^2] >> 5;
            if (last == 0b100 || last == 0b101) // Three byte group
            {
                if (bytes.Length < 3)
                    return false;

                last = bytes[^3] >> 4;
                if (last != 0b1110)
                    return false;
            }
            else if (last != 0b110) // Two byte group
            {
                return false;
            }
        }

        ref byte @ref = ref MemoryMarshal.GetReference(bytes);
        ref byte end = ref Unsafe.Add(ref @ref, bytes.Length);
        while (Unsafe.IsAddressLessThan(ref @ref, ref end))
        {
            int header = @ref >> 4;
            if (header < 0b1000) // One byte
            {
            }
            else if (header < 0b1110) // Two bytes
            {
                @ref = ref Unsafe.Add(ref @ref, 1);
                if ((@ref >> 6) != 0b10)
                    return false;
            }
            else if (header == 0b1110) // Three bytes
            {
                @ref = ref Unsafe.Add(ref @ref, 1);
                if ((@ref >> 6) != 0b10)
                    return false;
                @ref = ref Unsafe.Add(ref @ref, 1);
                if ((@ref >> 6) != 0b10)
                    return false;
            }
            else // Invalid header
            {
                return false;
            }
            charCount++;
            @ref = ref Unsafe.Add(ref @ref, 1);
        }

        return true;
    }

    private static void GetStringCommon(ReadOnlySpan<byte> bytes, int stringLength, ref char destination)
    {
        if (bytes.Length == stringLength)
        {
            GetStringAsciiScalar(bytes, ref destination);
        }
        else
        {
            GetStringScalar(bytes, ref destination);
        }
    }

    private static void GetStringAsciiAvx2(ReadOnlySpan<byte> bytes, ref char destination)
    {
        Debug.Assert(BitConverter.IsLittleEndian);
        Debug.Assert(Avx2.IsSupported);

        ref byte target = ref Unsafe.As<char, byte>(ref destination);
        ref byte source = ref MemoryMarshal.GetReference(bytes);
        ref byte sourceEnd = ref Unsafe.Add(ref source, bytes.Length - 31);
        while (Unsafe.IsAddressLessThan(ref source, ref sourceEnd))
        {
            Vector256<byte> vector = Unsafe.As<byte, Vector256<byte>>(ref source);

            Vector256<byte> low = Avx2.UnpackLow(vector, Vector256<byte>.Zero);
            Unsafe.WriteUnaligned(ref target, low);
            target = ref Unsafe.Add(ref target, 32);

            Vector256<byte> high = Avx2.UnpackHigh(vector, Vector256<byte>.Zero);
            Unsafe.WriteUnaligned(ref target, high);
            target = ref Unsafe.Add(ref target, 32);

            source = ref Unsafe.Add(ref source, 32);
        }

        sourceEnd = ref Unsafe.Add(ref sourceEnd, 31);
        destination = ref Unsafe.As<byte, char>(ref target);
        while (Unsafe.IsAddressLessThan(ref source, ref sourceEnd))
        {
            destination = (char)source;

            source = ref Unsafe.Add(ref source, 1);
            destination = ref Unsafe.Add(ref destination, 1);
        }
    }

    private static void GetStringAsciiScalar(ReadOnlySpan<byte> bytes, ref char destination)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            destination = (char)bytes[i];
            destination = ref Unsafe.Add(ref destination, 1);
        }
    }

    private static void GetStringScalar(ReadOnlySpan<byte> bytes, ref char destination)
    {
        ref byte source = ref MemoryMarshal.GetReference(bytes);
        ref byte sourceEnd = ref Unsafe.Add(ref source, bytes.Length);
        while (Unsafe.IsAddressLessThan(ref source, ref sourceEnd))
        {
            int c = source;
            int header = c >> 4;
            if (header < 0b1000) // One byte
            {
                destination = (char)c;
            }
            else if (header < 0b1110) // Two bytes
            {
                c = (c & 0b0001_1111) << 6;

                source = ref Unsafe.Add(ref source, 1);
                c |= source & 0b0011_1111;

                destination = (char)c;
            }
            else // Three bytes
            {
                c = (c & 0b0000_1111) << 12;

                source = ref Unsafe.Add(ref source, 1);
                c |= (source & 0b0011_1111) << 6;

                source = ref Unsafe.Add(ref source, 1);
                c |= source & 0b0011_1111;

                destination = (char)c;
            }
            source = ref Unsafe.Add(ref source, 1);
            destination = ref Unsafe.Add(ref destination, 1);
        }
    }

    /// <summary>
    /// Calculates the number of bytes produced by encoding the specified character span, unless the number of bytes is more than the encoding supports.
    /// </summary>
    /// <param name="chars">The span that contains the set of characters to encode.</param>
    /// <param name="byteCount">The number of bytes produced by encoding the specified character span.</param>
    /// <returns><c>true</c> if calculating the number of bytes was successful; otherwise <c>false</c></returns>
    public static bool TryGetByteCount(ReadOnlySpan<char> chars, out int byteCount)
    {
        if (chars.Length > ushort.MaxValue) // Even for all-ASCII inputs, this produces > 65535 bytes
        {
            byteCount = default;
            return false;
        }

        if (chars.IsEmpty)
        {
            byteCount = 0;
            return true;
        }

        if (Avx2.IsSupported)
        {
            byteCount = GetByteCountAvx2(chars);
            return byteCount <= ushort.MaxValue;
        }
        else
        {
            byteCount = GetByteCountScalar(chars);
            return byteCount <= ushort.MaxValue;
        }
    }

    private static int GetByteCountAvx2(ReadOnlySpan<char> chars)
    {
        Debug.Assert(Avx2.IsSupported);
        Debug.Assert(chars.Length <= ushort.MaxValue); // Ensure that the counter can't overflow

        const short TwoBytesBorder = (0x0080 >> 1) - 1;
        const short ThreeBytesBorder = (0x0800 >> 1) - 1;

        int byteCount = chars.Length; // Count all characters that will produce at least one byte

        ref byte ptr = ref Unsafe.As<char, byte>(ref MemoryMarshal.GetReference(chars));
        ref byte ptrEnd = ref Unsafe.Add(ref ptr, chars.Length * sizeof(char) - 31);

        Vector256<short> counter = Vector256<short>.Zero;
        for (; Unsafe.IsAddressLessThan(ref ptr, ref ptrEnd); ptr = ref Unsafe.Add(ref ptr, 32))
        {
            // Transform all 0x0000s to 0x0080s
            Vector256<ushort> vustr = Unsafe.ReadUnaligned<Vector256<ushort>>(ref ptr);
            Vector256<ushort> mask = Avx2.CompareEqual(vustr, Vector256<ushort>.Zero);
            Vector256<ushort> blend = Avx2.BlendVariable(vustr, Vector256.Create((ushort)0x0080), mask);

            Vector256<short> vstr = Avx2.ShiftRightLogical(blend, 1).AsInt16();
            counter = Avx2.Subtract(counter, Avx2.CompareGreaterThan(vstr, Vector256.Create(TwoBytesBorder))); // Count all characters that will produce at least two bytes
            counter = Avx2.Subtract(counter, Avx2.CompareGreaterThan(vstr, Vector256.Create(ThreeBytesBorder))); // Count all characters that will produce three bytes
        }

        counter = Avx2.HorizontalAdd(counter, counter); // 128
        counter = Avx2.HorizontalAdd(counter, counter); // 64

        // Here we must add by hand to avoid overflowing int16
        // Indexes of summing results are based on https://www.intel.com/content/www/us/en/docs/intrinsics-guide/index.html#text=_mm256_hadd_epi16&ig_expand=3839
        ref short counterRef = ref Unsafe.As<Vector256<short>, short>(ref counter);
        byteCount += counterRef;
        byteCount += Unsafe.Add(ref counterRef, 1);
        byteCount += Unsafe.Add(ref counterRef, 8);
        byteCount += Unsafe.Add(ref counterRef, 9);

        // Count the rest as scalars
        ptrEnd = ref Unsafe.Add(ref ptrEnd, 31);
        while (Unsafe.IsAddressLessThan(ref ptr, ref ptrEnd))
        {
            ushort c = Unsafe.ReadUnaligned<ushort>(ref ptr);
            if (c >= 0x80 || c == 0)
                byteCount += (c >= 0x800) ? 2 : 1;
            ptr = ref Unsafe.Add(ref ptr, sizeof(ushort));
        }
        return byteCount;
    }

    private static int GetByteCountScalar(ReadOnlySpan<char> chars)
    {
        int byteCount = chars.Length;
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (c >= 0x80 || c == 0)
                byteCount += (c >= 0x800) ? 2 : 1;
        }
        return byteCount;
    }
}
