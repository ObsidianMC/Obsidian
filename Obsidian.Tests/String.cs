using Obsidian.Net;

using System.Collections.Generic;

using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

public class String
{
    [Fact(DisplayName = "Equal strings")]
    public void Same()
    {
        FixtureCases.AssertAll(StringData, row => (string)row[0], row => AssertRoundTrips((string)row[0]));
    }

    private static void AssertRoundTrips(string expectedValue)
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
    private static IEnumerable<object[]> StringData =>
    [
        [string.Empty],
        ["a"],
        ["hello world"],
        ["é漢😀"],
        [new string('x', 128)]
    ];
}
