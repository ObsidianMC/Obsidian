using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;

namespace Obsidian.Net;
public sealed class EncryptedNetworkBuffer : NetworkBuffer
{
    private readonly AesCfbChopped aesCfbChopped;
    private readonly Aes aes;

    private readonly ICryptoTransform decryptor;
    private readonly ICryptoTransform encryptor;

    private BufferedBlockCipher encryptCipher { get; set; }
    private BufferedBlockCipher decryptCipher { get; set; }

    public EncryptedNetworkBuffer(byte[] key) : this(key, 0) { }
    public EncryptedNetworkBuffer(byte[] key, long capacity) : this(key, new byte[capacity]) { }
    public EncryptedNetworkBuffer(byte[] key, byte[] data) : base(data)
    {
        this.aesCfbChopped = new AesCfbChopped(key, key);

        this.aes = Aes.Create();

        aes.FeedbackSize = 8;
        aes.Mode = CipherMode.CFB;
        aes.Padding = PaddingMode.None;

        aes.Key = key;
        aes.IV = key;

        this.decryptor = aes.CreateDecryptor();
        this.encryptor = aes.CreateEncryptor();

        encryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesLightEngine(), 8));
        encryptCipher.Init(true, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

        decryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesLightEngine(), 8));
        decryptCipher.Init(false, new ParametersWithIV(new KeyParameter(key), key, 0, 16));
    }

    public override void Write(byte[] buffer, int offset, int size)
    {
        var encrypted = this.encryptor.TransformFinalBlock(buffer, offset, size);
        // var encrypted = encryptCipher.ProcessBytes(buffer, offset, size);

        base.Write(encrypted.AsSpan());
    }

    public override void WriteByte(byte value) => this.Write([value]);

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var input = buffer.ToArray();

        //var encrypted = new byte[input.Length];

        //var feedback = (byte[])this.aes.IV.Clone();
        //var block = new byte[16];

        //for (int i = 0; i < input.Length; i++)
        //{
        //    encryptor.TransformBlock(feedback, 0, 16, block, 0);
        //    byte xor = (byte)(block[0] ^ input[i]);
        //    encrypted[i] = xor;

        //    // Shift feedback 1 byte left and append new byte
        //    Buffer.BlockCopy(feedback, 1, feedback, 0, 15);
        //    feedback[15] = xor;
        //}

        //this.aes.IV = feedback;
        //this.aes.Key = feedback;

        var encrypted = this.aesCfbChopped.Encrypt(input);

        var encrypted2 = encryptCipher.ProcessBytes(input, 0, buffer.Length);

        if (encrypted.SequenceEqual(encrypted2))
            Debugger.Break();
        else
            Debugger.Break();

        base.Write(encrypted.AsSpan());
    }

    protected override byte[] ReadUntil(int size)
    {
        ValidateOffset();

        if (size == 0)
            return [];

        var decrypted = this.aesCfbChopped.Decrypt(this.data, this.offset, size);
        var decrypted2 = decryptCipher.ProcessBytes(this.data, this.offset, size);

        if (decrypted.SequenceEqual(decrypted2))
            Debugger.Break();
        else
            Debugger.Break();


        this.offset += decrypted.Length;
        this.BytesPending -= decrypted.Length;

        return decrypted;
    }

    public override void Dispose()
    {
        this.aesCfbChopped.Dispose();
        this.aes.Dispose();
        this.encryptor.Dispose();
        this.decryptor.Dispose();

        base.Dispose();
    }
}
