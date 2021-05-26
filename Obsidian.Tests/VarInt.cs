using Obsidian.IO;
using Obsidian.Net;

using System.Collections.Generic;

using Xunit;

namespace Obsidian.Tests
{
    public class VarInt
    {
        [MemberData(nameof(VarIntData))]
        [Theory(DisplayName = "Serialization of VarInt")]
        public void Serialize(int input, byte[] expectedOutput)
        {
            var writer = MemoryWriter.WithBuffer(5);
            writer.WriteVarInt(input);

            byte[] actualBytes = writer.Memory.ToArray();

            Assert.InRange(writer.BytesWritten, 1, 5);
            Assert.Equal(expectedOutput, actualBytes);
        }
        
        [MemberData(nameof(VarIntData))]
        [Theory(DisplayName = "Serialization of VarInts", Timeout = 100)]
        public async void SerializeAsync(int input, byte[] bytes)
        {
            using var stream = new MinecraftStream();

            await stream.WriteVarIntAsync(input);

            byte[] actualBytes = stream.ToArray();

            Assert.InRange(actualBytes.Length, 1, 5);
            Assert.Equal(bytes, actualBytes);
        }

        [MemberData(nameof(VarIntData))]
        [Theory(DisplayName = "Deserialization of VarInts", Timeout = 100)]
        public async void DeserializeAsync(int input, byte[] bytes)
        {
            using var stream = new MinecraftStream(bytes);

            int varInt = await stream.ReadVarIntAsync();

            Assert.Equal(input, varInt);
        }

        public static IEnumerable<object[]> VarIntData => new List<object[]>
        {
            new object[] { -2147483648, new byte[] { 0x80, 0x80, 0x80, 0x80, 0x08 } },
            new object[] { -1,          new byte[] { 0xff, 0xff, 0xff, 0xff, 0x0f } },
            new object[] { 2147483647,  new byte[] { 0xff, 0xff, 0xff, 0xff, 0x07 } },
            new object[] { 255,         new byte[] { 0xff, 0x01 } },
            new object[] { 128,         new byte[] { 0x80, 0x01 } },
            new object[] { 127,         new byte[] { 0x7f } },
            new object[] { 2,           new byte[] { 0x02 } },
            new object[] { 1,           new byte[] { 0x01 } },
            new object[] { 0,           new byte[] { 0x00 } },
        };
    }
}