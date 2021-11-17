
using Obsidian.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public class Encryption
{
    const int testDataLength = 1024;

    [MemberData(nameof(RandomData))]
    [Theory]
    public async Task TestEncryption(byte[] testData)
    {
        var random = new Random();
        var sharedKey = new byte[32];
        random.NextBytes(sharedKey);

        await using var memoryStream = new MemoryStream();
        await using var stream = new AesStream(memoryStream, sharedKey);

        await stream.WriteAsync(testData);
        memoryStream.Position = 0;

        var incomingRandomData = new byte[testDataLength];
        await stream.ReadAsync(incomingRandomData, 0, incomingRandomData.Length);

        Assert.Equal(testData, incomingRandomData);
    }

    public static IEnumerable<object[]> RandomData
    {
        get {
            var random = new Random();
            var values = new List<object[]>();

            for (int i = 0; i < 32; i++)
            {
                var randomData = new byte[testDataLength];
                random.NextBytes(randomData);
                values.Add(new object[] { randomData });
            }

            return values;
        }
    }
}
