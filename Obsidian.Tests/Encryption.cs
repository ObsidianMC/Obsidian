
using Obsidian.Net;
using System;
using System.Collections.Generic;
using Xunit;

namespace Obsidian.Tests;

public class Encryption
{
    const int testDataLength = 1024;

    [MemberData(nameof(RandomData))]
    [Theory]
    public void TestEncryption(byte[] testData)
    {
        var random = new Random();
        var sharedKey = new byte[32];
        random.NextBytes(sharedKey);

        using var buffer = new EncryptedNetworkBuffer(sharedKey);

        buffer.Write(testData);

        buffer.Reset();

        using var incomingRandomData = buffer.Read(testDataLength);

        Assert.Equal(testData, incomingRandomData.Data);
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
                values.Add([randomData]);
            }

            return values;
        }
    }
}
