
using Obsidian.Net;
using System;
using System.Collections.Generic;
using Xunit;

namespace Obsidian.Tests;

public class Encryption
{
    const int testDataLength = 1024;

    [Fact]
    public void SwappingSendBuffersPreservesTheEncryptionStream()
    {
        var key = new byte[16];
        using var first = new EncryptedNetworkBuffer(key);
        using var second = new EncryptedNetworkBuffer(key, first);
        using var receiver = new NetworkBuffer();
        byte[] plaintext = [1, 2, 3, 4, 5, 6];
        first.Write(plaintext.AsSpan(0, 2));
        second.Write(plaintext.AsSpan(2, 2));
        receiver.Write(first.AsSpan(0, first.Size));
        receiver.Write(second.AsSpan(0, second.Size));
        first.Clear();
        first.Write(plaintext.AsSpan(4, 2));
        receiver.Write(first.AsSpan(0, first.Size));
        using var decryptor = new AesCfbBlockCipher(key);
        Assert.Equal(plaintext, decryptor.Decrypt(receiver.AsSpan(0, receiver.Size), 0, receiver.Size));
    }

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
