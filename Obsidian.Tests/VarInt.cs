using Obsidian.Net;

using System.Collections.Generic;

using Xunit;

namespace Obsidian.Tests;

public class VarInt
{
    [MemberData(nameof(VarIntData))]
    [Theory(DisplayName = "Serialization of VarInts")]
    public void Serialize(int input, byte[] bytes)
    {
        using var stream = new NetworkBuffer();

        stream.WriteVarInt(input);

        stream.Reset();

        var actualBytes = stream.Read(bytes.Length);

        Assert.InRange(stream.Size, 1, 5);
        Assert.Equal(bytes, actualBytes.Data);
    }

    [MemberData(nameof(VarIntData))]
    [Theory(DisplayName = "Deserialization of VarInts")]
    public void Deserialize(int input, byte[] bytes)
    {
        using var stream = new NetworkBuffer(bytes);

        int varInt = stream.ReadVarInt();

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
