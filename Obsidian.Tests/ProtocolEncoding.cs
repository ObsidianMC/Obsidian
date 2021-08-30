using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Obsidian.IO;
using Xunit;

namespace Obsidian.Tests
{
    public class ProtocolEncoding
    {
        private readonly Random random = new Random();
        
        [Fact]
        public void Byte()
        {
            var value = (byte)random.Next(byte.MinValue, byte.MaxValue);
            var value2 = (byte)random.Next(byte.MinValue, byte.MaxValue);
            using var writer = ProtocolWriter.WithPool(ArrayPool<byte>.Shared);
            writer.WriteByte(value);
            writer.WriteByte(value2);
            var reader = new ProtocolReader(writer.Memory);
            var rode = reader.ReadByte();
            var rode2 = reader.ReadByte();
            Assert.Equal(value, rode);
            Assert.Equal(value2, rode2);
        }
        
        [Fact]
        public void Int16()
        {
            var value = (short)random.Next(short.MinValue, short.MaxValue);
            using var writer = ProtocolWriter.WithPool(ArrayPool<byte>.Shared);
            writer.WriteInt16(value);
            var reader = new ProtocolReader(writer.Memory);
            var rode = reader.ReadInt16();
            Assert.Equal(value, rode);
        }
        
        [Fact]
        public void UInt16()
        {
            var value = (ushort)random.Next(ushort.MinValue, ushort.MaxValue);
            using var writer = ProtocolWriter.WithPool(ArrayPool<byte>.Shared);
            writer.WriteUInt16(value);
            var reader = new ProtocolReader(writer.Memory);
            var rode = reader.ReadUInt16();
            Assert.Equal(value, rode);
        }
        
        [Fact]
        public void Int32()
        {
            var value = random.Next(int.MinValue, int.MaxValue);
            using var writer = ProtocolWriter.WithPool(ArrayPool<byte>.Shared);
            writer.WriteInt32(value);
            var reader = new ProtocolReader(writer.Memory);
            var rode = reader.ReadInt32();
            Assert.Equal(value, rode);
        }
        
        [Fact]
        public void UInt32()
        {
            var value = (uint)random.Next((int) uint.MinValue, int.MaxValue);
            using var writer = ProtocolWriter.WithPool(ArrayPool<byte>.Shared);
            writer.WriteUInt32(value);
            var reader = new ProtocolReader(writer.Memory);
            var rode = reader.ReadUInt32();
            Assert.Equal(value, rode);
        }
        
        [Fact]
        public void Int64()
        {
            var value = random.Next(int.MinValue, int.MaxValue);
            using var writer = ProtocolWriter.WithPool(ArrayPool<byte>.Shared);
            writer.WriteInt64(value);
            var reader = new ProtocolReader(writer.Memory);
            var rode = reader.ReadInt64();
            Assert.Equal(value, rode);
        }
        
        [Fact]
        public void UInt64()
        {
            var value = (uint)random.Next((int) uint.MinValue, int.MaxValue);
            using var writer = ProtocolWriter.WithPool(ArrayPool<byte>.Shared);
            writer.WriteUInt64(value);
            var reader = new ProtocolReader(writer.Memory);
            var rode = reader.ReadUInt64();
            Assert.Equal(value, rode);
        }
        
        [Fact]
        public void Float()
        {
            Span<byte> span = stackalloc byte[sizeof(float)];
            random.NextBytes(span);
            var value = BitConverter.ToSingle(span);
            using var writer = ProtocolWriter.WithPool(ArrayPool<byte>.Shared);
            writer.WriteFloat(value);
            var reader = new ProtocolReader(writer.Memory);
            var rode = reader.ReadFloat();
            Assert.Equal(value, rode);
        }
        
        [Fact]
        public void Double()
        {
            Span<byte> span = stackalloc byte[sizeof(double)];
            random.NextBytes(span);
            var value = BitConverter.ToDouble(span);
            using var writer = ProtocolWriter.WithPool(ArrayPool<byte>.Shared);
            writer.WriteDouble(value);
            var reader = new ProtocolReader(writer.Memory);
            var rode = reader.ReadDouble();
            Assert.Equal(value, rode);
        }
        
        [Fact]
        public void VarString()
        {
            var lenght = random.Next(1, short.MaxValue);
            var buff = new byte[lenght];
            RandomNumberGenerator.Fill(buff);
            
            var value = new string(Encoding.UTF8.GetString(buff));
            using var writer = ProtocolWriter.WithPool(ArrayPool<byte>.Shared);
            writer.WriteVarString(value);
            var reader = new ProtocolReader(writer.Memory);
            var rode = reader.ReadVarString();
            Assert.Equal(value, rode);
        }
        
        [Fact]
        public void UShortString()
        {
            var lenght = random.Next(1, short.MaxValue);
            var buff = new byte[lenght];
            RandomNumberGenerator.Fill(buff);
            
            var value = new string(Encoding.UTF8.GetString(buff));
            using var writer = ProtocolWriter.WithPool(ArrayPool<byte>.Shared);
            writer.WriteUShortString(value);
            var reader = new ProtocolReader(writer.Memory);
            var rode = reader.ReadUShortString();
            Assert.Equal(value, rode);
        }
        
        [Fact]
        public void Guid()
        {
            var value = System.Guid.NewGuid();
            using var writer = ProtocolWriter.WithPool(ArrayPool<byte>.Shared);
            writer.WriteGuid(value);
            var reader = new ProtocolReader(writer.Memory);
            var rode = reader.ReadGuid();
            Assert.Equal(value, rode);
        }
        
        [Fact]
        public void VarInt()
        {
            var value = random.Next();
            using var writer = ProtocolWriter.WithPool(ArrayPool<byte>.Shared);
            writer.WriteVarInt(value);
            var reader = new ProtocolReader(writer.Memory);
            var rode = reader.ReadVarInt();
            Assert.Equal(value, rode);
        }
        
        [Fact]
        public void VarLong()
        {
            Span<byte> span = stackalloc byte[sizeof(long)];
            random.NextBytes(span);
            var value = BitConverter.ToInt64(span);
            using var writer = ProtocolWriter.WithPool(ArrayPool<byte>.Shared);
            writer.WriteDouble(value);
            var reader = new ProtocolReader(writer.Memory);
            var rode = reader.ReadDouble();
            Assert.Equal(value, rode);
        }
    }
}