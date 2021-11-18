using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Obsidian.IO;

/// <summary>
/// Utility struct for writing data to a <see cref="byte"/>[] buffer.
/// </summary>
/// <remarks>
/// Instances of this struct should be obtained with <see cref="WithBuffer(int)"/> or <see cref="WithBuffer(byte[])"/>.
/// When you are done writing, call
/// <see cref="Dispose"/> to release the buffer back to the pool. <br />
/// </remarks>
/// <seealso cref="ArrayPool{T}"/>
/// <seealso cref="MemoryMeasure"/>
/// <seealso cref="IDisposable"/>
[DebuggerDisplay("{" + nameof(GetDebuggerDisplay) + "(),nq}")]
[SkipLocalsInit]
public struct ProtocolWriter : IDisposable
{
    /// <summary>
    /// Returns a <see cref="ReadOnlyMemory{T}"/> representing the writer's buffer content
    /// </summary>
    /// <remarks>
    /// When this property is in use, writing is discouraged as it may require renting a new buffer
    /// </remarks>
    public ReadOnlyMemory<byte> Memory => new(buffer, 0, index);

    public Span<byte> Span => new(buffer, 0, index);

    /// <summary>
    /// Returns the buffer being used by the writer
    /// </summary>
    public byte[] Buffer => buffer;
    public int BytesWritten => index;

    private ArrayPool<byte>? pool;
    private byte[] buffer;
    private int index;

    /// <summary>
    /// Creates new <see cref="ProtocolWriter"/> with buffer of specific length using the <see cref="ArrayPool{T}.Shared"/> <see cref="ArrayPool{T}"/>.
    /// </summary>
    /// <param name="minimumLength">Minimum length of the buffer.</param>
    public static ProtocolWriter WithBuffer(int minimumLength)
    {
        return new ProtocolWriter
        {
            pool = ArrayPool<byte>.Shared,
            buffer = ArrayPool<byte>.Shared.Rent(minimumLength),
            index = 0
        };
    }

    /// <summary>
    /// Creates new <see cref="ProtocolWriter"/> with specific buffer.
    /// </summary>
    /// <param name="buffer">Rented buffer to be used for writing.</param>
    public static ProtocolWriter WithBuffer(byte[] buffer)
    {
        return new ProtocolWriter
        {
            buffer = buffer,
            index = 0
        };
    }

    /// <summary>
    /// Creates new <see cref="ProtocolWriter"/> with specific buffer.
    /// </summary>
    /// <param name="buffer">Rented buffer to be used for writing.</param>
    /// <param name="offset">Starting point for writing.</param>
    public static ProtocolWriter WithBuffer(byte[] buffer, int offset)
    {
        return new ProtocolWriter
        {
            buffer = buffer,
            index = offset
        };
    }

    /// <summary>
    /// Creates a new <see cref="ProtocolWriter"/> using the specified <see cref="ArrayPool{T}"/> to rent buffers
    /// </summary>
    /// <param name="pool"></param>
    /// <returns></returns>
    public static ProtocolWriter WithPool(ArrayPool<byte> pool)
    {
        return new ProtocolWriter()
        {
            pool = pool,
            buffer = pool.Rent(128),
            index = 0
        };
    }

    /// <summary>
    /// Releases buffer back to the array pool.
    /// </summary>
    [SuppressMessage("ReSharper", "ConditionIsAlwaysTrueOrFalse", Justification = "Can be null if the writer was not instantiated correctly")]
    public void Dispose()
    {
        if (buffer is null || pool is null) return;
        ArrayPool<byte>.Shared.Return(buffer);
#pragma warning disable 8625
        buffer = null;
#pragma warning restore 8625
    }

    #region Buffer management
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasCapacity(int neededCapacity)
    {
        return index + neededCapacity < buffer.Length;
    }

    public void EnsureCapacity(int neededCapacity)
    {
        if (!HasCapacity(neededCapacity))
            Resize(neededCapacity);
    }

    public unsafe void Resize(int minimum)
    {
        if (pool is null)
            throw new NullReferenceException("This writer does not have a pool assigned");

        var newLength = buffer.Length < minimum
            ? (buffer.Length + (minimum - buffer.Length)) * 2
            : buffer.Length * 2;

        var newBuffer = pool.Rent(newLength);
        fixed (byte* ptrSrc = buffer)
        {
            fixed (byte* ptrDest = newBuffer)
            {
                System.Buffer.MemoryCopy(ptrSrc, ptrDest, newBuffer.Length, index);
            }
        }
        pool.Return(buffer);
        buffer = newBuffer;
    }
    #endregion

    #region Writing primitives
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(byte value)
    {
        EnsureCapacity(sizeof(byte));
        Unsafe.WriteUnaligned(ref GetBufferRef(), value);
        index++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSByte(sbyte value) => WriteByte((byte)value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBool(bool value) => WriteByte((byte)(value ? 0x01 : 0x00));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt16(short value)
    {
        EnsureCapacity(sizeof(short));
        if (BitConverter.IsLittleEndian)
            value = BinaryPrimitives.ReverseEndianness(value);

        Unsafe.WriteUnaligned(ref GetBufferRef(), value);
        index += sizeof(short);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt16(ushort value)
    {
        EnsureCapacity(sizeof(ushort));
        if (BitConverter.IsLittleEndian)
            value = BinaryPrimitives.ReverseEndianness(value);

        Unsafe.WriteUnaligned(ref GetBufferRef(), value);
        index += sizeof(ushort);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32(int value)
    {
        EnsureCapacity(sizeof(int));
        if (BitConverter.IsLittleEndian)
            value = BinaryPrimitives.ReverseEndianness(value);

        Unsafe.WriteUnaligned(ref GetBufferRef(), value);
        index += sizeof(int);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32(uint value)
    {
        EnsureCapacity(sizeof(uint));
        if (BitConverter.IsLittleEndian)
            value = BinaryPrimitives.ReverseEndianness(value);

        Unsafe.WriteUnaligned(ref GetBufferRef(), value);
        index += sizeof(uint);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64(long value)
    {
        EnsureCapacity(sizeof(long));
        if (BitConverter.IsLittleEndian)
            value = BinaryPrimitives.ReverseEndianness(value);

        Unsafe.WriteUnaligned(ref GetBufferRef(), value);
        index += sizeof(long);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64(ulong value)
    {
        EnsureCapacity(sizeof(ulong));
        if (BitConverter.IsLittleEndian)
            value = BinaryPrimitives.ReverseEndianness(value);

        Unsafe.WriteUnaligned(ref GetBufferRef(), value);
        index += sizeof(ulong);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFloat(float value)
    {
        EnsureCapacity(sizeof(float));
        if (BitConverter.IsLittleEndian)
            Unsafe.WriteUnaligned(ref GetBufferRef(), BinaryPrimitives.ReverseEndianness(Unsafe.As<float, int>(ref value)));
        else Unsafe.WriteUnaligned(ref GetBufferRef(), value);
        index += sizeof(float);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDouble(double value)
    {
        EnsureCapacity(sizeof(double));
        if (BitConverter.IsLittleEndian)
            Unsafe.WriteUnaligned(ref GetBufferRef(), BinaryPrimitives.ReverseEndianness(Unsafe.As<double, long>(ref value)));
        else Unsafe.WriteUnaligned(ref GetBufferRef(), value);
        index += sizeof(double);
    }
    #endregion

    #region Writing commons
    /// <summary>
    /// Writes a <see cref="string"/> prefixed by VarInt.
    /// </summary>
    public void WriteVarString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            WriteByte(0);
            return;
        }

        var byteLength = Encoding.UTF8.GetByteCount(value);
        WriteVarInt(byteLength);
        EnsureCapacity(byteLength);
        Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, index);
        index += byteLength;
    }

    /// <summary>
    /// Writes a <see cref="string"/> prefixed by <see cref="ushort"/>.
    /// </summary>
    public void WriteUShortString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            WriteByte(0);
            return;
        }

        var byteLength = Encoding.UTF8.GetByteCount(value);
        if (byteLength > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), "String is too big");
        WriteUInt16((ushort)byteLength);
        EnsureCapacity(byteLength);
        Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, index);
        index += byteLength;
    }

    public void WriteGuid(Guid value)
    {
        EnsureCapacity(16);
        Unsafe.WriteUnaligned(ref GetBufferRef(), value);
        index += 16;
    }
    #endregion

    #region Writing arrays
    public unsafe void WriteSpan(ReadOnlySpan<byte> span)
    {
        EnsureCapacity(span.Length);
        fixed (byte* spanPtr = span)
        {
            fixed (byte* arrPtr = buffer)
            {
                System.Buffer.MemoryCopy(spanPtr, arrPtr + index, buffer.Length - index, span.Length);
            }
        }
        index += span.Length;
    }

    public void WriteInt64Span(ReadOnlySpan<long> value)
    {
        WriteSpan(MemoryMarshal.AsBytes(value));
    }
    #endregion

    #region Writing VarLength types
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteVarInt<T>(T value) where T : Enum
    {
        WriteVarInt(Unsafe.As<T, int>(ref value));
    }

    public void WriteVarInt(int value)
    {
        ref var source = ref GetBufferRef();
        ref var target = ref source;

        var unsigned = (uint)value;

    WRITE_BYTE:
        target = (byte)(unsigned & 127);
        unsigned >>= 7;

        if (unsigned != 0)
        {
            target |= 128;
            target = ref Unsafe.Add(ref target, 1);
            goto WRITE_BYTE;
        }

        index += (Unsafe.ByteOffset(ref source, ref target) + 1).ToInt32();
    }

    public unsafe void WriteVarLong(long value)
    {
        ref var source = ref GetBufferRef();
        ref var target = ref source;

        var unsigned = (ulong)value;

    WRITE_BYTE:
        target = (byte)(unsigned & 127);
        unsigned >>= 7;

        if (unsigned != 0)
        {
            target |= 128;
            target = ref Unsafe.Add(ref target, 1);
            goto WRITE_BYTE;
        }

        index += (Unsafe.ByteOffset(ref source, ref target) + 1).ToInt32();
    }


    #endregion

    #region Utilities


    /// <summary>
    /// Copies the writer's content to a new <see cref="byte"/>[]
    /// </summary>
    /// <returns></returns>
    public byte[] ToArray()
    {
        var arr = new byte[index];
        Memory.CopyTo(arr);
        return arr;
    }

    public unsafe bool TryCopyTo(Span<byte> span)
    {
        if (span.Length < index)
            return false;

        fixed (byte* spanPtr = span)
        {
            fixed (byte* arrPtr = buffer)
            {
                System.Buffer.MemoryCopy(arrPtr, spanPtr, span.Length, index);
            }
        }

        return true;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref byte GetBufferRef() => ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(buffer), index);

    private readonly string GetDebuggerDisplay()
    {
        return $"ProtocolWriter [{index.ToString()}/{buffer.Length.ToString()}]";
    }
    #endregion
}
