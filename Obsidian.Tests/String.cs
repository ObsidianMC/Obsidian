using Obsidian.Net;

using System.Collections.Generic;

using Xunit;

namespace Obsidian.Tests;

public class String
{
    [MemberData(nameof(StringData))]
    [Theory(DisplayName = "Equal strings")]
    public void Same(string expectedValue)
    {
        using var stream = new NetworkBuffer();

        stream.WriteString(expectedValue);

        stream.Reset(); //Reset to beginning for read

        string actualValue = stream.ReadString();

        Assert.Equal(expectedValue, actualValue);
        Assert.Equal(stream.Size, stream.Offset);

        using var encrypted = new EncryptedNetworkBuffer(new byte[16]);
        encrypted.WriteString(expectedValue);
        encrypted.Reset();

        Assert.Equal(expectedValue, encrypted.ReadString());
        Assert.Equal(encrypted.Size, encrypted.Offset);
    }

    // Keep five deterministic cases: empty, short ASCII, spaces, multibyte UTF-8 and a two-byte length prefix.
    public static IEnumerable<object[]> StringData =>
    [
        [string.Empty],
        ["a"],
        ["hello world"],
        ["é漢😀"],
        [new string('x', 128)]
    ];
}
