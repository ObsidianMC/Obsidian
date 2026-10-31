using System;
using Obsidian.Net;
using Obsidian.Utilities;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

public class VarInt
{
    [Fact]
    public void VarIntsMatchWire()
    {
        (int Value, byte[] Bytes)[] cases =
        [
            (0, new byte[] { 0x00 }),
            (127, new byte[] { 0x7f }),
            (128, new byte[] { 0x80, 0x01 }),
            (1 << 14, new byte[] { 0x80, 0x80, 0x01 }),
            (1 << 21, new byte[] { 0x80, 0x80, 0x80, 0x01 }),
            (int.MaxValue, new byte[] { 0xff, 0xff, 0xff, 0xff, 0x07 }),
            (-1, new byte[] { 0xff, 0xff, 0xff, 0xff, 0x0f }),
            (int.MinValue, new byte[] { 0x80, 0x80, 0x80, 0x80, 0x08 })
        ];

        FixtureCases.AssertAll(
            cases,
            row => $"value={row.Value}, bytes={Convert.ToHexString(row.Bytes)}",
            row => AssertVarIntsMatchWire(row.Value, row.Bytes));
    }

    private static void AssertVarIntsMatchWire(int value, byte[] bytes)
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
