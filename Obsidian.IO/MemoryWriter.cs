using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Obsidian.IO
{
    /// <summary>
    /// Utility struct for writing data to a <see cref="byte"/>[] buffer.
    /// </summary>
    /// <remarks>
    /// Instances of this struct should be obtained with <see cref="WithBuffer(int)"/> or <see cref="WithBuffer(byte[])"/>,
    /// where <see cref="byte"/>[] belongs to <see cref="ArrayPool{byte}.Shared"/>. When you are done writing, call
    /// <see cref="Dispose"/> to release the buffer back to the pool. <br />
    /// <see cref="MemoryWriter"/> does <b>NOT</b> do array bounds checks before writing to it. You must make sure that
    /// the buffer is large enough, or do the bound checks yourself. You can use <see cref="HasCapacity(int)"/> and
    /// <see cref="Expand"/>. You can use <see cref="MemoryMeasure"/> to measure data length.
    /// </remarks>
    /// <seealso cref="ArrayPool{T}"/>
    /// <seealso cref="MemoryMeasure"/>
    /// <seealso cref="IDisposable"/>
    [DebuggerDisplay("{" + nameof(GetDebuggerDisplay) + "(),nq}")]
    public struct MemoryWriter : IDisposable
    {
        public ReadOnlyMemory<byte> Memory => new(buffer, 0, (int)index);
        public byte[] Buffer => buffer;
        public long BytesWritten => index;

        private byte[] buffer;
        private nint index;

        /// <summary>
        /// Creates new <see cref="MemoryWriter"/> with buffer of specific length.
        /// </summary>
        /// <param name="minimumLength">Minimum length of the buffer.</param>
        public static MemoryWriter WithBuffer(int minimumLength)
        {
            return new MemoryWriter
            {
                buffer = ArrayPool<byte>.Shared.Rent(minimumLength),
                index = 0
            };
        }

        /// <summary>
        /// Creates new <see cref="MemoryWriter"/> with specific buffer.
        /// </summary>
        /// <param name="buffer">Rented buffer to be used for writing.</param>
        public static MemoryWriter WithBuffer(byte[] buffer)
        {
            return new MemoryWriter
            {
                buffer = buffer,
                index = 0
            };
        }

        /// <summary>
        /// Creates new <see cref="MemoryWriter"/> with specific buffer.
        /// </summary>
        /// <param name="buffer">Rented buffer to be used for writing.</param>
        /// <param name="offset">Starting point for writing.</param>
        public static MemoryWriter WithBuffer(byte[] buffer, int offset)
        {
            return new MemoryWriter
            {
                buffer = buffer,
                index = offset
            };
        }

        /// <summary>
        /// Releases buffer back to the array pool.
        /// </summary>
        public void Dispose()
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                buffer = null;
            }
        }

        #region Buffer management
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasCapacity(int neededCapacity)
        {
            return index + neededCapacity < buffer.Length;
        }

        public void Expand()
        {
            var newBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
            System.Buffer.BlockCopy(buffer, 0, newBuffer, 0, (int)index);
            ArrayPool<byte>.Shared.Return(buffer);
            buffer = newBuffer;
        }
        #endregion

        #region Writing primitives
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value)
        {
            GetRef() = value;
            index++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSByte(sbyte value)
        {
            GetRef<sbyte>() = value;
            index++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBool(bool value)
        {
            GetRef<bool>() = value;
            index++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteShort(short value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                value = BinaryPrimitives.ReverseEndianness(value);
            }
            GetRef<short>() = value;

            index += sizeof(short);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUShort(ushort value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                value = BinaryPrimitives.ReverseEndianness(value);
            }
            GetRef<ushort>() = value;

            index += sizeof(ushort);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt(int value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                value = BinaryPrimitives.ReverseEndianness(value);
            }
            GetRef<int>() = value;

            index += sizeof(int);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt(uint value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                value = BinaryPrimitives.ReverseEndianness(value);
            }
            GetRef<uint>() = value;

            index += sizeof(uint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteLong(long value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                value = BinaryPrimitives.ReverseEndianness(value);
            }
            GetRef<long>() = value;

            index += sizeof(long);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteULong(ulong value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                value = BinaryPrimitives.ReverseEndianness(value);
            }
            GetRef<ulong>() = value;

            index += sizeof(ulong);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFloat(float value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                GetRef<int>() = BinaryPrimitives.ReverseEndianness(Unsafe.As<float, int>(ref value));
            }
            else
            {
                GetRef<int>() = Unsafe.As<float, int>(ref value);
            }

            index += sizeof(int);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteDouble(double value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                GetRef<long>() = BinaryPrimitives.ReverseEndianness(Unsafe.As<double, long>(ref value));
            }
            else
            {
                GetRef<long>() = Unsafe.As<double, long>(ref value);
            }

            index += sizeof(long);
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
            Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, (int)index);
            index += byteLength;
        }

        public void WriteUShortString(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteShort(0);
                return;
            }

            ushort byteLength = checked((ushort)Encoding.UTF8.GetByteCount(value));
            WriteUShort(byteLength);
            Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, (int)index);
            index += byteLength;
        }

        public void WriteGuid(Guid value)
        {
            BigEndianGuid.Write(ref GetRef(), ref value);
            index += 16;
        }
        #endregion

        #region Writing arrays
        public void WriteByteArray(byte[] array)
        {
            System.Buffer.BlockCopy(array, 0, buffer, (int)index, array.Length);
            index += array.Length;
        }

        public void WriteLongArray(long[] array)
        {
            ref long target = ref GetRef<long>();
            for (int i = 0; i < array.Length; i++)
            {
                long value = array[i];

                if (!BitConverter.IsLittleEndian)
                {
                    value = BinaryPrimitives.ReverseEndianness(value);
                }

                target = value;
                target = ref Unsafe.Add(ref target, 1);
            }
            index += array.Length * sizeof(long);
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
            ref byte source = ref GetRef();
            ref byte target = ref source;

            uint unsigned = (uint)value;

        WRITE_BYTE:
            target = (byte)(unsigned & 127);
            unsigned >>= 7;

            if (unsigned != 0)
            {
                target |= 128;
                target = ref Unsafe.Add(ref target, 1);
                goto WRITE_BYTE;
            }

            index += Unsafe.ByteOffset(ref source, ref target) + 1;
        }

        public void WriteVarLong(long value)
        {
            ref byte source = ref GetRef();
            ref byte target = ref source;

            ulong unsigned = (ulong)value;

        WRITE_BYTE:
            target = (byte)(unsigned & 127);
            unsigned >>= 7;

            if (unsigned != 0)
            {
                target |= 128;
                target = ref Unsafe.Add(ref target, 1);
                goto WRITE_BYTE;
            }

            index += Unsafe.ByteOffset(ref source, ref target) + 1;
        }
        #endregion

        #region Utilities
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref byte GetRef()
        {
            return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(buffer), index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref T GetRef<T>() where T : struct
        {
            return ref Unsafe.As<byte, T>(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(buffer), index));
        }

        private readonly string GetDebuggerDisplay()
        {
            return $"MemoryWriter [{index}/{buffer.Length}]";
        }
        #endregion
    }
}
