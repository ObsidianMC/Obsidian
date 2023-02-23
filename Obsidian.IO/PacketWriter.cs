using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Obsidian.IO;

public struct PacketWriter<TOutput> where TOutput : IPacketOutput
{
    private TOutput output;

    public TOutput Output => output;


    public PacketWriter(TOutput output)
    {
        this.output = output;
    }

    /// <summary>
    /// Writes any value that implements <see cref="IPacketWritable"/>.
    /// </summary>
    /// <param name="value">The value to write</param>
    /// <typeparam name="T">The type of the value</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public void Write<T>(in T value) where T : IPacketWritable => value.WriteToPacketWriter(ref this);

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    private unsafe void WriteValue<T>(ref T value) where T : unmanaged
    {
        if (output.TryGetDirectBuffer(sizeof(T), out var buffer))
        {
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(buffer), value);
        }
        else
        {
            Span<byte> span = stackalloc byte[sizeof(T)];
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(span), value);
            WriteBytes(span);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    private unsafe void WriteValue<T>(T value) where T : unmanaged
    {
        if (output.TryGetDirectBuffer(sizeof(T), out var buffer))
        {
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(buffer), value);
        }
        else
        {
            Span<byte> span = stackalloc byte[sizeof(T)];
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(span), value);
            WriteBytes(span);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public void WriteBytes(in Span<byte> bytes) => output.WriteBytes(bytes);

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public void WriteByte(sbyte b) => WriteUByte((byte)b);

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public void WriteUByte(byte b) => output.WriteByte(b);


    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public void WriteShort(short s)
    {
        WriteValue(BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(s) : s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public void WriteUShort(ushort s)
    {
        WriteValue(BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(s) : s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public void WriteInt(int i)
    {
        WriteValue(BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(i) : i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public void WriteLong(long l)
    {
        WriteValue(BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(l) : l);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public void WriteFloat(float f)
    {
        WriteValue(BitConverter.IsLittleEndian
            ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToInt32Bits(f)))
            : f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public void WriteDouble(double d)
    {
        WriteValue(BitConverter.IsLittleEndian
            ? BitConverter.Int64BitsToDouble(BinaryPrimitives.ReverseEndianness(BitConverter.DoubleToInt64Bits(d)))
            : d);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public unsafe void WriteVarInt(int value)
    {
        var ptr = stackalloc byte[5];
        var current = ptr;

        var unsigned = (uint)value;

        WRITE_BYTE:
        *current = (byte)(unsigned & 127);
        unsigned >>= 7;

        if (unsigned != 0)
        {
            *current |= 128;
            current += 1;
            goto WRITE_BYTE;
        }

        var len = (int)(current - ptr) + 1;
        WriteBytes(new Span<byte>(ptr, len));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public unsafe void WriteVarLong(long value)
    {
        var ptr = stackalloc byte[10];
        var current = ptr;

        var unsigned = (ulong)value;

        WRITE_BYTE:
        *current = (byte)(unsigned & 127);
        unsigned >>= 7;

        if (unsigned != 0)
        {
            *current |= 128;
            current += 1;
            goto WRITE_BYTE;
        }

        var len = (int)(current - ptr) + 1;
        WriteBytes(new Span<byte>(ptr, len));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public void WriteUuid(Guid uuid)
    {
        WriteValue(ref uuid);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public void WriteVarString(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            WriteVarInt(0);
            return;
        }

        var bytesCount = Encoding.UTF8.GetByteCount(value);
        WriteVarInt(bytesCount);
        WriteString(value, bytesCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public void WriteShortString(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            WriteShort(0);
            return;
        }

        var bytesCount = Encoding.UTF8.GetByteCount(value);
        WriteShort((short)bytesCount);
        WriteString(value, bytesCount);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    private void WriteString(ReadOnlySpan<char> value, int bytes)
    {
        if (output.TryGetDirectBuffer(bytes, out var buffer))
        {
            Encoding.UTF8.GetBytes(value, buffer);
        }
        else
        {
            var bytesBuffer = bytes <= 256 ? stackalloc byte[256] : new byte[bytes];
            Encoding.UTF8.GetBytes(value, bytesBuffer);
            WriteBytes(bytesBuffer);
        }
    }
}
