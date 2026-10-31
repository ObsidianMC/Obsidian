using System;
using Obsidian.Net;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

public class Boolean
{
    [Fact]
    public void BooleansMatchWire()
    {
        (bool Value, byte[] Bytes)[] cases =
        [
            (false, new byte[] { 0x00 }),
            (true, new byte[] { 0x01 })
        ];

        FixtureCases.AssertAll(
            cases,
            row => $"value={row.Value}, bytes={Convert.ToHexString(row.Bytes)}",
            row => AssertBooleansMatchWire(row.Value, row.Bytes));
    }

    private static void AssertBooleansMatchWire(bool value, byte[] bytes)
    {
        using var writer = new NetworkBuffer();
        writer.WriteBoolean(value);
        Assert.Equal(bytes, writer.AsSpan(0, writer.Size).ToArray());

        using var reader = new NetworkBuffer(bytes);
        Assert.Equal(value, reader.ReadBoolean());
        Assert.Equal(bytes.Length, reader.Offset);
    }
}
