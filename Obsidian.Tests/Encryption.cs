
using Obsidian.Net;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Xunit;

namespace Obsidian.Tests;

public class Encryption
{
    const int testDataLength = 1024;

    [Fact]
    public void CipherContinuesTheStreamAcrossCalls()
    {
        var key = new byte[16];
        Random.Shared.NextBytes(key);
        var plaintext = new byte[1200];
        Random.Shared.NextBytes(plaintext);

        // The protocol's CFB8 one byte at a time: each byte is XORed with the first byte of the encrypted register.
        using var aes = Aes.Create();
        aes.Key = key;
        var register = (byte[])key.Clone();
        var expected = new byte[plaintext.Length];
        for (var i = 0; i < plaintext.Length; i++)
        {
            expected[i] = (byte)(plaintext[i] ^ aes.EncryptEcb(register, PaddingMode.None)[0]);
            register.AsSpan(1).CopyTo(register);
            register[^1] = expected[i];
        }

        // Splits shorter, equal to and longer than a block, so every way the register carries over is crossed.
        int[] splits = [1, 15, 16, 17, 3, 500, 648];
        using var encryptor = new AesCfbBlockCipher(key);
        using var decryptor = new AesCfbBlockCipher(key);
        var encrypted = (byte[])plaintext.Clone();
        var decrypted = new List<byte>();
        var start = 0;
        foreach (var length in splits)
        {
            encryptor.EncryptInPlace(encrypted.AsSpan(start, length));
            decrypted.AddRange(decryptor.Decrypt(encrypted, start, length));
            start += length;
        }

        Assert.Equal(plaintext.Length, start);
        Assert.Equal(expected, encrypted);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void FramerEncryptsTheStreamFromItsEncryptionFrame()
    {
        var key = new byte[16];
        Random.Shared.NextBytes(key);
        var large = new byte[600];
        Random.Shared.NextBytes(large);
        byte[][] bodies = [[1, 2, 3], large, [4]];

        // The login's order: a plain packet, then encryption, then compressed framing.
        var framer = new PacketFramer();
        using var output = new NetworkBuffer();
        framer.Write(output, new OutgoingFrame(Body(bodies[0]), CompressionThreshold: -1));
        framer.Write(output, new OutgoingFrame(Body: null, CompressionThreshold: -1, EncryptionKey: key));
        framer.Write(output, new OutgoingFrame(Body(bodies[1]), CompressionThreshold: 256));
        framer.Write(output, new OutgoingFrame(Body(bodies[2]), CompressionThreshold: 256));

        using var expected = new NetworkBuffer();
        expected.WriteFrame(bodies[0], -1);
        var plainLength = expected.Offset;
        expected.WriteFrame(bodies[1], 256);
        expected.WriteFrame(bodies[2], 256);

        Assert.Equal(expected.Offset, output.Offset);
        Assert.Equal(expected.AsSpan(0, plainLength).ToArray(), output.AsSpan(0, plainLength).ToArray());

        using var decryptor = new AesCfbBlockCipher(key);
        var decrypted = decryptor.Decrypt(output.AsSpan(0, output.Offset), plainLength, output.Offset - plainLength);
        Assert.Equal(expected.AsSpan(plainLength, expected.Offset - plainLength).ToArray(), decrypted);

        static NetworkBuffer Body(byte[] bytes)
        {
            var body = new NetworkBuffer();
            body.Write(bytes);
            return body;
        }
    }

    [MemberData(nameof(RandomData))]
    [Theory]
    public void TestEncryption(byte[] testData)
    {
        var random = new Random();
        // The protocol's shared secret is 16 bytes, and it also serves as the IV, which must match AES's block size.
        var sharedKey = new byte[16];
        random.NextBytes(sharedKey);

        using var buffer = new EncryptedNetworkBuffer(sharedKey);

        buffer.Write(testData);

        buffer.Reset();

        using var incomingRandomData = buffer.Read(testDataLength);

        Assert.Equal(testData, incomingRandomData.GetBuffer());
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
