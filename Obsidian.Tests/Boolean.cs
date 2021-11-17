using Obsidian.Net;

using System.Collections.Generic;

using Xunit;

namespace Obsidian.Tests;

public class Boolean
{
    [MemberData(nameof(BooleanData))]
    [Theory(DisplayName = "Serialization of booleans", Timeout = 100)]
    public async void SerializeAsync(bool input, byte @byte)
    {
        using var stream = new MinecraftStream();

        await stream.WriteBooleanAsync(input);

        byte[] actualBytes = stream.ToArray();

        Assert.Single(actualBytes);

        Assert.Equal(@byte, actualBytes[0]);
    }

    [MemberData(nameof(BooleanData))]
    [Theory(DisplayName = "Deserialization of booleans", Timeout = 100)]
    public async void DeserializeAsync(bool input, byte @byte)
    {
        using var stream = new MinecraftStream(new[] { @byte });

        bool boolean = await stream.ReadBooleanAsync();

        Assert.Equal(input, boolean);
    }

    public static IEnumerable<object[]> BooleanData => new List<object[]>
        {
            new object[] { false, 0x00 },
            new object[] { true,  0x01 },
        };
}
