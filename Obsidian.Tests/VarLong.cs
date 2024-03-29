﻿using Obsidian.Net;

using System.Collections.Generic;

using Xunit;

namespace Obsidian.Tests;

public class VarLong
{
    [MemberData(nameof(VarLongData))]
    [Theory(DisplayName = "Serialization of VarLongs", Timeout = 100)]
    public async void SerializeAsync(long input, byte[] bytes)
    {
        using var stream = new MinecraftStream();

        await stream.WriteVarLongAsync(input);

        byte[] actualBytes = stream.ToArray();

        Assert.InRange(actualBytes.Length, 1, 10);
        Assert.Equal(bytes, actualBytes);
    }

    [MemberData(nameof(VarLongData))]
    [Theory(DisplayName = "Deserialization of VarLongs", Timeout = 100)]
    public async void DeserializeAsync(long input, byte[] bytes)
    {
        using var stream = new MinecraftStream(bytes);

        long varLong = await stream.ReadVarLongAsync();

        Assert.Equal(input, varLong);
    }

    public static IEnumerable<object[]> VarLongData => new List<object[]>
        {
            new object[] { 0,                       new byte[] { 0x00 } },
            new object[] { 1,                       new byte[] { 0x01 } },
            new object[] { 2,                       new byte[] { 0x02 } },
            new object[] { 127,                     new byte[] { 0x7f } },
            new object[] { 128,                     new byte[] { 0x80, 0x01 } },
            new object[] { 255,                     new byte[] { 0xff, 0x01 } },
            new object[] { 2147483647,              new byte[] { 0xff, 0xff, 0xff, 0xff, 0x07 } },
            new object[] { 9223372036854775807,     new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x7f } },
            new object[] { -1,                      new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x01 } },
            new object[] { -2147483648,             new byte[] { 0x80, 0x80, 0x80, 0x80, 0xf8, 0xff, 0xff, 0xff, 0xff, 0x01 } },
            new object[] { -9223372036854775808,    new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 } }
        };
}
