using Obsidian.Net;
using Xunit;

namespace Obsidian.Tests;

public class Boolean
{
    [Theory]
    [InlineData(false, new byte[] { 0x00 })]
    [InlineData(true, new byte[] { 0x01 })]
    public void BooleansMatchWire(bool value, byte[] bytes)
    {
        using var writer = new NetworkBuffer();
        writer.WriteBoolean(value);
        Assert.Equal(bytes, writer.AsSpan(0, writer.Size).ToArray());

        using var reader = new NetworkBuffer(bytes);
        Assert.Equal(value, reader.ReadBoolean());
        Assert.Equal(bytes.Length, reader.Offset);
    }
}
