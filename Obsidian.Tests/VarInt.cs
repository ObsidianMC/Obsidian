using Obsidian.Net;
using Obsidian.Utilities;
using Xunit;

namespace Obsidian.Tests;

public class VarInt
{
    [Theory]
    [InlineData(0, new byte[] { 0x00 })]
    [InlineData(127, new byte[] { 0x7f })]
    [InlineData(128, new byte[] { 0x80, 0x01 })]
    [InlineData(1 << 14, new byte[] { 0x80, 0x80, 0x01 })]
    [InlineData(1 << 21, new byte[] { 0x80, 0x80, 0x80, 0x01 })]
    [InlineData(int.MaxValue, new byte[] { 0xff, 0xff, 0xff, 0xff, 0x07 })]
    [InlineData(-1, new byte[] { 0xff, 0xff, 0xff, 0xff, 0x0f })]
    [InlineData(int.MinValue, new byte[] { 0x80, 0x80, 0x80, 0x80, 0x08 })]
    public void VarIntsMatchWire(int value, byte[] bytes)
    {
        Assert.Equal(bytes.Length, value.GetVarIntLength());

        using var writer = new NetworkBuffer();
        writer.WriteVarInt(value);
        Assert.Equal(bytes, writer.AsSpan(0, writer.Size).ToArray());

        using var reader = new NetworkBuffer(bytes);
        Assert.Equal(value, reader.ReadVarInt());
        Assert.Equal(bytes.Length, reader.Offset);
    }
}
