using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Obsidian.IO
{
    /// <summary>
    /// Utility struct for reading data from a <see cref="System.Memory{T}"/>
    /// </summary>
    [DebuggerDisplay("{" + nameof(GetDebuggerDisplay) + "(),nq}")]
    public struct ProtocolReader
    {
        /// <summary>
        /// Returns the <see cref="ReadOnlyMemory{T}"/> being read from
        /// </summary>
        public readonly ReadOnlyMemory<byte> Memory;

        /// <summary>
        /// Returns a <see cref="ReadOnlySpan{T}"/> starting at the current <see cref="Position"/>
        /// </summary>
        public ReadOnlySpan<byte> CurrentSpan => Memory.Span[index..];
        
        /// <summary>
        /// The reader's current position
        /// </summary>
        public int Position
        {
            get => index;
            set => index = value;
        }
        
        private int index;

        public ProtocolReader(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
            index = 0;
        }

        /// <summary>
        /// Reads a <see cref="Span{T}"/> to the target <see cref="Span{T}"/>
        /// </summary>
        /// <param name="span">The <see cref="Span{T}"/> to write to</param>
        /// <exception cref="IndexOutOfRangeException">There is not enough data in the reader's <see cref="Memory"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadSpan(Span<byte> span)
        {
            if (Memory.Length - index < span.Length)
                throw new IndexOutOfRangeException("Cannot read past memory end");
            
            CurrentSpan.CopyTo(span);
            index += span.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            if (index >= Memory.Length)
                throw new IndexOutOfRangeException("Cannot read past memory end");

            return Memory.Span[index++];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sbyte ReadSByte() => (sbyte) ReadByte();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBool() => ReadByte() > 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public short ReadInt16()
        {
            Span<byte> span = stackalloc byte[sizeof(short)];
            ReadSpan(span);
            return BinaryPrimitives.ReadInt16BigEndian(span);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadUInt16()
        {
            Span<byte> span = stackalloc byte[sizeof(ushort)];
            ReadSpan(span);
            return BinaryPrimitives.ReadUInt16BigEndian(span);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt32()
        {
            Span<byte> span = stackalloc byte[sizeof(int)];
            ReadSpan(span);
            return BinaryPrimitives.ReadInt32BigEndian(span);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt32()
        {
            Span<byte> span = stackalloc byte[sizeof(uint)];
            ReadSpan(span);
            return BinaryPrimitives.ReadUInt32BigEndian(span);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadInt64()
        {
            Span<byte> span = stackalloc byte[sizeof(long)];
            ReadSpan(span);
            return BinaryPrimitives.ReadInt64BigEndian(span);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadUInt64()
        {
            Span<byte> span = stackalloc byte[sizeof(ulong)];
            ReadSpan(span);
            return BinaryPrimitives.ReadUInt64BigEndian(span);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadFloat()
        {
            Span<byte> span = stackalloc byte[sizeof(float)];
            ReadSpan(span);
            return BinaryPrimitives.ReadSingleBigEndian(span);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ReadDouble()
        {
            Span<byte> span = stackalloc byte[sizeof(double)];
            ReadSpan(span);
            return BinaryPrimitives.ReadDoubleBigEndian(span);
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

        public string ReadString(int length)
        {
            if (length == 0) return string.Empty;

            if (length <= 1024)
            {
                Span<byte> span = stackalloc byte[length];
                ReadSpan(span);
                return Encoding.UTF8.GetString(span);
            }
            
            var pool = ArrayPool<byte>.Shared;
            var arr = pool.Rent(length);

            var arrSpan = arr.AsSpan(0, length);
            ReadSpan(arrSpan);
            
            var str = Encoding.UTF8.GetString(arrSpan);
            pool.Return(arr);
            return str;
        }

        public Guid ReadGuid()
        {
            Span<byte> span = stackalloc byte[16];
            ReadSpan(span);
            return new Guid(span);
        }

        public void ReadInt64Span(Span<long> span)
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = ReadInt64();
        }


        private readonly string GetDebuggerDisplay()
        {
            return $"ProtocolReader [{index.ToString()}/{Memory.Length.ToString()}]";
        }
    }
}
