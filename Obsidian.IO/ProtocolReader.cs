using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Obsidian.IO;

/// <summary>
/// Utility struct for reading data from a <see cref="System.Memory{T}"/>
/// </summary>
[DebuggerDisplay("{" + nameof(GetDebuggerDisplay) + "(),nq}")]
[SkipLocalsInit]
public struct ProtocolReader
{
    /// <summary>
    /// Returns the <see cref="byte"/>[] being read from
    /// </summary>
    public byte[] Buffer => buffer;

    /// <summary>
    /// Returns a <see cref="ReadOnlySpan{T}"/> starting at the current <see cref="Position"/>
    /// </summary>
    public ReadOnlySpan<byte> CurrentSpan => new(buffer, index, length - index);

    /// <summary>
    /// The reader's current position
    /// </summary>
    public int Position
    {
        get => index;
        set => index = value;
    }

    /// <summary>
    /// The buffer's length
    /// </summary>
    public int Length => length;

    private readonly byte[] buffer;
    private readonly int length;
    private int index;

    /// <summary>
    /// Creates a <see cref="ProtocolReader"/> using the underlying array of the <see cref="ReadOnlyMemory{T}"/>
    /// </summary>
    /// <param name="memory"><see cref="ReadOnlyMemory{T}"/> created using an array</param>
    /// <exception cref="InvalidOperationException">The provided <see cref="ReadOnlyMemory{T}"/> was not created from an array</exception>
    public ProtocolReader(ReadOnlyMemory<byte> memory)
    {
        if (!MemoryMarshal.TryGetArray(memory, out var segment))
            throw new InvalidOperationException("Cannot get an array from the ReadOnlyMemory");

        buffer = segment.Array!;
        length = segment.Count;
        index = segment.Offset;
    }

    /// <summary>
    /// Creates a <see cref="ProtocolReader"/> using the provided <see cref="ArraySegment{T}"/>
    /// </summary>
    /// <param name="segment">Buffer to read from</param>
    public ProtocolReader(ArraySegment<byte> segment)
    {
        buffer = segment.Array!;
        length = segment.Count;
        index = segment.Offset;
    }

    /// <summary>
    /// Reads a <see cref="Span{T}"/> to the target <see cref="Span{T}"/>
    /// </summary>
    /// <param name="span">The <see cref="Span{T}"/> to write to</param>
    /// <exception cref="IndexOutOfRangeException">There is not enough data in the reader's <see cref="Buffer"/></exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void ReadSpan(Span<byte> span)
    {
        EnsureSize(span.Length);

        fixed (byte* sptr = span)
        {
            fixed (byte* bptr = buffer)
            {
                System.Buffer.MemoryCopy(bptr + index, sptr, span.Length, span.Length);
            }
        }
        index += span.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureSize(int required)
    {
        if (index + required > length)
            ThrowOutOfRange();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOutOfRange()
    {
        throw new IndexOutOfRangeException("Cannot read past buffer's end");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte()
    {
        EnsureSize(1);
        return Unsafe.ReadUnaligned<byte>(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(buffer), index++));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sbyte ReadSByte() => (sbyte)ReadByte();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBool() => ReadByte() > 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short ReadInt16()
    {
        var v = Unsafe.ReadUnaligned<short>(ref GetBufferRef());
        if (BitConverter.IsLittleEndian)
            v = BinaryPrimitives.ReverseEndianness(v);

        index += sizeof(short);
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadUInt16()
    {
        var v = Unsafe.ReadUnaligned<ushort>(ref GetBufferRef());
        if (BitConverter.IsLittleEndian)
            v = BinaryPrimitives.ReverseEndianness(v);

        index += sizeof(ushort);
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32()
    {
        var v = Unsafe.ReadUnaligned<int>(ref GetBufferRef());
        if (BitConverter.IsLittleEndian)
            v = BinaryPrimitives.ReverseEndianness(v);

        index += sizeof(int);
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadUInt32()
    {
        var v = Unsafe.ReadUnaligned<uint>(ref GetBufferRef());
        if (BitConverter.IsLittleEndian)
            v = BinaryPrimitives.ReverseEndianness(v);

        index += sizeof(uint);
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadInt64()
    {
        var v = Unsafe.ReadUnaligned<long>(ref GetBufferRef());
        if (BitConverter.IsLittleEndian)
            v = BinaryPrimitives.ReverseEndianness(v);

        index += sizeof(long);
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadUInt64()
    {
        var v = Unsafe.ReadUnaligned<ulong>(ref GetBufferRef());
        if (BitConverter.IsLittleEndian)
            v = BinaryPrimitives.ReverseEndianness(v);

        index += sizeof(ulong);
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadFloat()
    {
        var v = Unsafe.ReadUnaligned<int>(ref GetBufferRef());
        if (BitConverter.IsLittleEndian)
            v = BinaryPrimitives.ReverseEndianness(v);

        index += sizeof(float);
        return Unsafe.As<int, float>(ref v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ReadDouble()
    {
        var v = Unsafe.ReadUnaligned<long>(ref GetBufferRef());
        if (BitConverter.IsLittleEndian)
            v = BinaryPrimitives.ReverseEndianness(v);

        index += sizeof(long);
        return Unsafe.As<long, double>(ref v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadVarInt()
    {
        var numRead = 0;
        var result = 0;
        byte read;

        do
        {
            read = ReadByte();
            var value = read & 0b01111111;
            result |= value << (7 * numRead);

            numRead++;
            if (numRead > 5)
            {
                throw new InvalidOperationException("VarInt is too big");
            }
        } while ((read & 0b10000000) != 0);

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadVarLong()
    {
        var numRead = 0;
        var result = 0L;
        byte read;

        do
        {
            read = ReadByte();
            var value = read & 0b01111111;
            result |= (long)value << (7 * numRead);

            numRead++;
            if (numRead > 10)
            {
                throw new InvalidOperationException("VarLong is too big");
            }
        } while ((read & 0b10000000) != 0);

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadVarString() => ReadString(ReadVarInt());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadUShortString() => ReadString(ReadUInt16());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadString(int count)
    {
        if (count == 0) return string.Empty;

        if (count <= 1024)
        {
            Span<byte> span = stackalloc byte[count];
            ReadSpan(span);
            return Encoding.UTF8.GetString(span);
        }

        var pool = ArrayPool<byte>.Shared;
        var arr = pool.Rent(count);

        var arrSpan = arr.AsSpan(0, count);
        ReadSpan(arrSpan);

        var str = Encoding.UTF8.GetString(arrSpan);
        pool.Return(arr);
        return str;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Guid ReadGuid()
    {
        var id = Unsafe.ReadUnaligned<Guid>(ref GetBufferRef());
        index += 16;
        return id;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadInt64Array(Span<long> span)
    {
        for (var i = 0; i < span.Length; i++)
            span[i] = ReadInt64();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref byte GetBufferRef() => ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(buffer), index);

    private readonly string GetDebuggerDisplay()
    {
        return $"ProtocolReader [{index}/{length}]";
    }
}
