using Obsidian.Net;
using System.Collections.Generic;
using Xunit;

namespace Obsidian.Tests;

public class Boolean
{
    [MemberData(nameof(BooleanData))]
    [Theory(DisplayName = "Serialization of booleans")]
    public  void Serialize(bool input, byte @byte)
    {
        using var stream = new NetworkBuffer();

        stream.WriteBoolean(input);

        byte[] actualBytes = stream.ToArray();

        Assert.Single(actualBytes);

        Assert.Equal(@byte, actualBytes[0]);
    }

    [MemberData(nameof(BooleanData))]
    [Theory(DisplayName = "Deserialization of booleans")]
    public void Deserialize(bool input, byte @byte)
    {
        using var stream = new NetworkBuffer([@byte]);

        bool boolean = stream.ReadBoolean();

        Assert.Equal(input, boolean);
    }

    public static IEnumerable<object[]> BooleanData => new List<object[]>
        {
            new object[] { false, 0x00 },
            new object[] { true,  0x01 },
        };
}
