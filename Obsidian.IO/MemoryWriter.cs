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
    /// the buffer is large enough, or do the bound checks yourself. You can call <see cref="EnsureCapacity(int)"/>, which
    /// expands the buffer, if it's not large enough to hold specified amount of bytes. You can use <see cref="MemoryMeasure"/>
    /// to measure data length in bytes.
    /// </remarks>
    /// <seealso cref="ArrayPool{T}"/>
    /// <seealso cref="MemoryMeasure"/>
    /// <seealso cref="IDisposable"/>
    [DebuggerDisplay("{" + nameof(GetDebuggerDisplay) + "(),nq}")]
    public struct MemoryWriter : IDisposable
    {
        public byte[] Buffer => buffer;
        public long BytesWritten => index;

        private byte[] buffer;
        private nint index;

        private static readonly Encoding utf8 = Encoding.UTF8;

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
        public void EnsureCapacity(int neededCapacity)
        {
            if (index + neededCapacity >= buffer.Length)
            {
                var newBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                System.Buffer.BlockCopy(buffer, 0, newBuffer, 0, (int)index);
                ArrayPool<byte>.Shared.Return(buffer);
                buffer = newBuffer;
            }
        }
        #endregion

        #region Writing primitives
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value)
        {
            buffer[index++] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSByte(sbyte value)
        {
            buffer[index++] = Unsafe.As<sbyte, byte>(ref value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBool(bool value)
        {
            buffer[index++] = Unsafe.As<bool, byte>(ref value);
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
            WriteInt(Unsafe.As<float, int>(ref value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteDouble(double value)
        {
            WriteLong(Unsafe.As<double, long>(ref value));
        }
        #endregion

        #region Writing commons
        /// <summary>
        /// Writes a <see cref="string"/> prefixed by VarInt.
        /// </summary>
        public void WriteVarString(string value)
        {
            if (value is null || value.Length == 0)
            {
                buffer[index++] = 0;
                return;
            }

            int byteLength = utf8.GetByteCount(value);
            WriteVarInt(byteLength);
            utf8.GetBytes(value, 0, value.Length, buffer, (int)index);
            index += byteLength;
        }

        public void WriteUShortString(string value)
        {
            if (value is null)
            {
                WriteShort(0);
            }

            ushort byteLength = checked((ushort)utf8.GetByteCount(value));
            WriteUShort(byteLength);
            utf8.GetBytes(value, 0, value.Length, buffer, (int)index);
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
            for (int i = 0; i < array.Length; i++)
            {
                WriteLong(array[i]);
            }
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
            var unsigned = (uint)value;

            do
            {
                var temp = (byte)(unsigned & 127);
                unsigned >>= 7;

                if (unsigned != 0)
                    temp |= 128;

                buffer[index++] = temp;
            }
            while (unsigned != 0);
        }

        public void WriteVarLong(long value)
        {
            var unsigned = (ulong)value;

            do
            {
                var temp = (byte)(unsigned & 127);

                unsigned >>= 7;

                if (unsigned != 0)
                    temp |= 128;

                buffer[index++] = temp;
            }
            while (unsigned != 0);
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

        private static unsafe ref byte GetLastByteRef<T>(ref T value) where T : unmanaged
        {
            return ref Unsafe.Add(ref Unsafe.As<T, byte>(ref value), sizeof(T) - 1);
        }

        private readonly string GetDebuggerDisplay()
        {
            return $"MemoryWriter [{index}/{buffer.Length}]";
        }
        #endregion
    }
}
