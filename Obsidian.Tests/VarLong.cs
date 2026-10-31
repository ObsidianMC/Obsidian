using System;
using Obsidian.Net;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

public class VarLong
{
    [Fact]
    public void VarLongsMatchWire()
    {
        (long Value, byte[] Bytes)[] cases =
        [
            (0L, new byte[] { 0x00 }),
            (127L, new byte[] { 0x7f }),
            (128L, new byte[] { 0x80, 0x01 }),
            (long.MaxValue, new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x7f }),
            (-1L, new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x01 }),
            (long.MinValue, new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 })
        ];

        FixtureCases.AssertAll(
            cases,
            row => $"value={row.Value}, bytes={Convert.ToHexString(row.Bytes)}",
            row => AssertVarLongsMatchWire(row.Value, row.Bytes));
    }

    private static void AssertVarLongsMatchWire(long value, byte[] bytes)
    {
        using var writer = new NetworkBuffer();
        writer.WriteVarLong(value);
        Assert.Equal(bytes, writer.AsSpan(0, writer.Size).ToArray());

        using var reader = new NetworkBuffer(bytes);
        Assert.Equal(value, reader.ReadVarLong());
        Assert.Equal(bytes.Length, reader.Offset);
    }
}
