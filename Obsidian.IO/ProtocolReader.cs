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
    /// Utility struct for reading data from a <see cref="System.Memory{T}"/>
    /// </summary>
    [DebuggerDisplay("{" + nameof(GetDebuggerDisplay) + "(),nq}")]
    [SkipLocalsInit]
    public struct ProtocolReader
    {
        /// <summary>
        /// Returns the <see cref="ReadOnlySpan{T}"/> being read from
        /// </summary>

        public unsafe ReadOnlySpan<byte> Span => new(buffer, length);
        /// <summary>
        /// Returns a <see cref="ReadOnlySpan{T}"/> starting at the current <see cref="Position"/>
        /// </summary>
        public unsafe ReadOnlySpan<byte> CurrentSpan => new(buffer + index, length - index);
        
        
        /// <summary>
        /// The reader's current position
        /// </summary>
        public int Position
        {
            get => index;
            set => index = value;
        }

        private readonly unsafe byte* buffer;
        private readonly int length;
        private int index;

        public unsafe ProtocolReader(byte* buffer, int length)
        {
            this.buffer = buffer;
            this.length = length;
            index = 0;
        }

        public ProtocolReader(ReadOnlyMemory<byte> memory) : this(memory.Span)
        {
            
        }

        public unsafe ProtocolReader(byte[] buffer)
        {
            this.buffer = (byte*) Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(buffer));
            length = buffer.Length;
            index = 0;
        }

        public unsafe ProtocolReader(ReadOnlySpan<byte> span)
        {

            fixed (byte* ptr = span)
                buffer = ptr;
            length = span.Length;
            index = 0;
        }

        /// <summary>
        /// Reads a <see cref="Span{T}"/> to the target <see cref="Span{T}"/>
        /// </summary>
        /// <param name="span">The <see cref="Span{T}"/> to write to</param>
        /// <exception cref="IndexOutOfRangeException">There is not enough data in the reader's <see cref="Span"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadSpan(Span<byte> span)
        {
            if (length - index < span.Length)
                throw new IndexOutOfRangeException("Cannot read past memory end");
            
            Span.Slice(index, span.Length).CopyTo(span);
            index += span.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            if (index >= length)
                throw new IndexOutOfRangeException("Cannot read past memory end");

            return Span[index++];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sbyte ReadSByte() => (sbyte) ReadByte();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBool() => ReadByte() > 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe short ReadInt16()
        {
            var v = Unsafe.ReadUnaligned<short>(ref buffer[index]);
            if (BitConverter.IsLittleEndian)
                v = BinaryPrimitives.ReverseEndianness(v);

            index += sizeof(short);
            return v;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ushort ReadUInt16()
        {
            var v = Unsafe.ReadUnaligned<ushort>(ref buffer[index]);
            if (BitConverter.IsLittleEndian)
                v = BinaryPrimitives.ReverseEndianness(v);

            index += sizeof(ushort);
            return v;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe int ReadInt32()
        {
            var v = Unsafe.ReadUnaligned<int>(ref buffer[index]);
            if (BitConverter.IsLittleEndian)
                v = BinaryPrimitives.ReverseEndianness(v);

            index += sizeof(int);
            return v;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe uint ReadUInt32()
        {
            var v = Unsafe.ReadUnaligned<uint>(ref buffer[index]);
            if (BitConverter.IsLittleEndian)
                v = BinaryPrimitives.ReverseEndianness(v);

            index += sizeof(uint);
            return v;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe long ReadInt64()
        {
            var v = Unsafe.ReadUnaligned<long>(ref buffer[index]);
            if (BitConverter.IsLittleEndian)
                v = BinaryPrimitives.ReverseEndianness(v);

            index += sizeof(long);
            return v;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ulong ReadUInt64()
        {
            var v = Unsafe.ReadUnaligned<ulong>(ref buffer[index]);
            if (BitConverter.IsLittleEndian)
                v = BinaryPrimitives.ReverseEndianness(v);

            index += sizeof(ulong);
            return v;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe float ReadFloat()
        {
            var v = Unsafe.ReadUnaligned<int>(ref buffer[index]);
            if (BitConverter.IsLittleEndian)
                v = BinaryPrimitives.ReverseEndianness(v);

            index += sizeof(float);
            return Unsafe.As<int, float>(ref v);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe double ReadDouble()
        {
            var v = Unsafe.ReadUnaligned<long>(ref buffer[index]);
            if (BitConverter.IsLittleEndian)
                v = BinaryPrimitives.ReverseEndianness(v);

            index += sizeof(long);
            return Unsafe.As<long, double>(ref v);
        }


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


        
        public string ReadVarString() => ReadString(ReadVarInt());

        public string ReadUShortString() => ReadString(ReadUInt16());

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
            var arr = pool.Rent(length);

            var arrSpan = arr.AsSpan(0, count);
            ReadSpan(arrSpan);
            
            var str = Encoding.UTF8.GetString(arrSpan);
            pool.Return(arr);
            return str;
        }

        public unsafe Guid ReadGuid()
        {
            var id = *(Guid*)(buffer + index);
            index += 16;
            return id;
        }

        public void ReadInt64Span(Span<long> span)
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = ReadInt64();
        }


        private readonly string GetDebuggerDisplay()
        {
            return $"ProtocolReader [{index.ToString()}/{length.ToString()}]";
        }
    }
}
