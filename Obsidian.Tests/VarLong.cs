using Obsidian.Net;
using Xunit;

namespace Obsidian.Tests;

public class VarLong
{
    [Theory]
    [InlineData(0L, new byte[] { 0x00 })]
    [InlineData(127L, new byte[] { 0x7f })]
    [InlineData(128L, new byte[] { 0x80, 0x01 })]
    [InlineData(long.MaxValue, new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x7f })]
    [InlineData(-1L, new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x01 })]
    [InlineData(long.MinValue, new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 })]
    public void VarLongsMatchWire(long value, byte[] bytes)
    {
        using var writer = new NetworkBuffer();
        writer.WriteVarLong(value);
        Assert.Equal(bytes, writer.AsSpan(0, writer.Size).ToArray());

        using var reader = new NetworkBuffer(bytes);
        Assert.Equal(value, reader.ReadVarLong());
        Assert.Equal(bytes.Length, reader.Offset);
    }
}
