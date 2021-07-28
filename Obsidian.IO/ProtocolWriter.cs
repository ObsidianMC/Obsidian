using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Obsidian.IO
{
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
    public struct ProtocolWriter : IDisposable
    {
        /// <summary>
        /// Returns a <see cref="ReadOnlyMemory{T}"/> representing the writer's buffer content
        /// </summary>
        /// <remarks>
        /// When this property is in use, writing is discouraged as it may require renting a new buffer
        /// </remarks>
        public ReadOnlyMemory<byte> Memory => new(buffer, 0, index);
        
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
                buffer =  ArrayPool<byte>.Shared.Rent(minimumLength),
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
            EnsureCapacity(1);
            buffer[index++] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSByte(sbyte value) => WriteByte((byte) value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBool(bool value) => WriteByte((byte) (value ? 0x01 : 0x00));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt16(short value)
        {

            Span<byte> span = stackalloc byte[sizeof(short)];
            BinaryPrimitives.WriteInt16BigEndian(span, value);
            WriteSpan(span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt16(ushort value)
        {
            Span<byte> span = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16BigEndian(span, value);
            WriteSpan(span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt32(int value)
        {
            Span<byte> span = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(span, value);
            WriteSpan(span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt32(uint value)
        {
            Span<byte> span = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(span, value);
            WriteSpan(span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt64(long value)
        {
            Span<byte> span = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64BigEndian(span, value);
            WriteSpan(span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt64(ulong value)
        {
            Span<byte> span = stackalloc byte[sizeof(ulong)];
            BinaryPrimitives.WriteUInt64BigEndian(span, value);
            WriteSpan(span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFloat(float value)
        {
            Span<byte> span = stackalloc byte[sizeof(float)];
            BinaryPrimitives.WriteSingleBigEndian(span, value);
            WriteSpan(span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteDouble(double value)
        {
            Span<byte> span = stackalloc byte[sizeof(double)];
            BinaryPrimitives.WriteDoubleBigEndian(span, value);
            WriteSpan(span);
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
                buffer[index++] = 0;
                return;
            }

            int byteLength = Encoding.UTF8.GetByteCount(value);
            WriteVarInt(byteLength);
            if (byteLength <= 1024)
            {
                Span<byte> span = stackalloc byte[byteLength];
                Encoding.UTF8.GetBytes(value, span);
                WriteSpan(span);
            }
            else
            {
                EnsureCapacity(byteLength);
                var written = Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, index);
                index += written;
            }
        }
        
        /// <summary>
        /// Writes a <see cref="string"/> prefixed by <see cref="ushort"/>.
        /// </summary>
        public void WriteUShortString(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteInt16(0);
                return;
            }
            
            var byteLength = checked((ushort)Encoding.UTF8.GetByteCount(value));
            WriteUInt16(byteLength);
            if (byteLength <= 1024)
            {
                Span<byte> span = stackalloc byte[byteLength];
                Encoding.UTF8.GetBytes(value, span);
                WriteSpan(span);
            }
            else
            {
                EnsureCapacity(byteLength);
                var written = Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, index);
                index += written;
            }
        }

        public void WriteGuid(Guid value)
        {
            Span<byte> span = stackalloc byte[16];
            value.TryWriteBytes(span); // Guid's binary representation is always big endian
            WriteSpan(span);
            
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

            for (var i = 0; i < value.Length; i++)
                WriteInt64(value[i]);
        }
        #endregion

        #region Writing VarLength types
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarInt<T>(T value) where T : Enum
        {
            WriteVarInt(Unsafe.As<T, int>(ref value));
        }

        public unsafe void WriteVarInt(int value)
        {
            var ptr = stackalloc byte[5];

            var unsigned = (uint)value;
            byte size = 0;
            do
            {
                var temp = (byte) (unsigned & 127);
                unsigned >>= 7;

                if (unsigned != 0)
                    temp |= 128;

                ptr[size++] = temp;
            } while (unsigned != 0);
            
            WriteSpan(new ReadOnlySpan<byte>(ptr, size));
        }

        public unsafe void WriteVarLong(long value)
        {
            var ptr = stackalloc byte[10];

            var unsigned = (ulong)value;
            byte size = 0;
            do
            {
                var temp = (byte) (unsigned & 127);
                unsigned >>= 7;

                if (unsigned != 0)
                    temp |= 128;

                ptr[size++] = temp;
            } while (unsigned != 0);
            
            WriteSpan(new ReadOnlySpan<byte>(ptr, size));
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

        private readonly string GetDebuggerDisplay()
        {
            return $"ProtocolWriter [{index.ToString()}/{buffer.Length.ToString()}]";
        }
        #endregion
    }
}
