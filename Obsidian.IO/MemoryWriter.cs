using Obsidian.API;
using System;
using System.Buffers;
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

        public void WriteShort(short value)
        {
            ref byte target = ref GetRef();
            ref byte source = ref GetLastByteRef(ref value);

            target = source; // 1
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 2

            index += sizeof(short);
        }

        public void WriteUShort(ushort value)
        {
            ref byte target = ref GetRef();
            ref byte source = ref GetLastByteRef(ref value);

            target = source; // 1
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 2

            index += sizeof(ushort);
        }

        public void WriteInt(int value)
        {
            ref byte target = ref GetRef();
            ref byte source = ref GetLastByteRef(ref value);

            target = source; // 1
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 2
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 3
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 4

            index += sizeof(int);
        }

        public void WriteUInt(uint value)
        {
            ref byte target = ref GetRef();
            ref byte source = ref GetLastByteRef(ref value);

            target = source; // 1
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 2
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 3
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 4

            index += sizeof(uint);
        }

        public void WriteLong(long value)
        {
            ref byte target = ref GetRef();
            ref byte source = ref GetLastByteRef(ref value);

            target = source; // 1
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 2
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 3
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 4
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 5
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 6
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 7
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 8

            index += sizeof(long);
        }

        public void WriteULong(ulong value)
        {
            ref byte target = ref GetRef();
            ref byte source = ref GetLastByteRef(ref value);

            target = source; // 1
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 2
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 3
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 4
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 5
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 6
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 7
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 8

            index += sizeof(ulong);
        }

        public void WriteFloat(float value)
        {
            ref byte target = ref GetRef();
            ref byte source = ref GetLastByteRef(ref value);

            target = source; // 1
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 2
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 3
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 4

            index += sizeof(float);
        }

        public void WriteDouble(double value)
        {
            ref byte target = ref GetRef();
            ref byte source = ref GetLastByteRef(ref value);

            target = source; // 1
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 2
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 3
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 4
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 5
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 6
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 7
            target = ref Unsafe.Add(ref target, 1);
            source = ref Unsafe.Add(ref source, -1);
            target = source; // 8

            index += sizeof(double);
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

        #region Writing Obsidian.API types
        public void WriteAngle(Angle value)
        {
            buffer[index++] = value.Value;
        }

        public void WriteChatMessage(IChatMessage chatMessage)
        {
            WriteVarString(chatMessage.ToString());
        }

        public void WriteVector(Vector value)
        {
            var val = (long)(value.X & 0x3FFFFFF) << 38;
            val |= (long)(value.Z & 0x3FFFFFF) << 12;
            val |= (long)(value.Y & 0xFFF);

            WriteLong(val);
        }

        public void WriteAbsolutePosition(Vector value)
        {
            WriteDouble(value.X);
            WriteDouble(value.Y);
            WriteDouble(value.Z);
        }

        public void WriteVectorF(VectorF value)
        {
            var val = (long)((int)value.X & 0x3FFFFFF) << 38;
            val |= (long)((int)value.Z & 0x3FFFFFF) << 12;
            val |= (long)((int)value.Y & 0xFFF);

            WriteLong(val);
        }

        public void WriteAbsolutePositionF(VectorF value)
        {
            WriteDouble(value.X);
            WriteDouble(value.Y);
            WriteDouble(value.Z);
        }

        public void WriteVelocity(Velocity value)
        {
            WriteShort(value.X);
            WriteShort(value.Y);
            WriteShort(value.Z);
        }

        public void WriteSoundPosition(SoundPosition value)
        {
            WriteInt(value.X);
            WriteInt(value.Y);
            WriteInt(value.Z);
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
