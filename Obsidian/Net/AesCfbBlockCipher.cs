using System.Security.Cryptography;

namespace Obsidian.Net;
public sealed class AesCfbBlockCipher : IDisposable
{
    private const int BlockSize = 16;

    private readonly Aes aes;
    private readonly ICryptoTransform transform;

    private readonly byte[] iv;
    private readonly byte[] block;

    public AesCfbBlockCipher(byte[] key)
    {
        this.iv = new byte[key.Length];

        key.CopyTo(this.iv);

        this.block = new byte[BlockSize];

        this.aes = Aes.Create();
        this.aes.Mode = CipherMode.ECB;
        this.aes.Padding = PaddingMode.None;
        this.aes.FeedbackSize = 8;

        this.aes.Key = key;
        this.aes.IV = iv;

        this.transform = aes.CreateEncryptor();
    }

    public Span<byte> Encrypt(ReadOnlySpan<byte> buffer)
    {
        var output = new byte[buffer.Length];
        for (int i = 0; i < output.Length; i++)
        {
            this.transform.TransformBlock(this.iv, 0, BlockSize, this.block, 0);

            var cipherByte = (byte)(buffer[i] ^ this.block[0]);

            Buffer.BlockCopy(this.iv, 1, this.iv, 0, 15);

            this.iv[15] = cipherByte;

            output[i] = cipherByte;
        }

        return output;
    }

    public Span<byte> Encrypt(byte[] buffer, int offset, int count)
    {
        var output = new byte[count];
        for (int i = 0; i < count; i++)
        {
            var plainByte = buffer[offset + i];

            this.transform.TransformBlock(this.iv, 0, BlockSize, this.block, 0);

            var cipherByte = (byte)(plainByte ^ this.block[0]);

            Buffer.BlockCopy(this.iv, 1, this.iv, 0, 15);

            this.iv[15] = cipherByte;
            output[i] = cipherByte;
        }

        return output;
    }

    public byte[] Decrypt(ReadOnlySpan<byte> buffer, int offset, int count)
    {
        var output = new byte[count];
        for (int i = 0; i < count; i++)
        {
            var cipherByte = buffer[offset + i];

            this.transform.TransformBlock(this.iv, 0, BlockSize, this.block, 0);
            var plainByte = (byte)(cipherByte ^ this.block[0]);

            Buffer.BlockCopy(this.iv, 1, this.iv, 0, 15);
            this.iv[15] = cipherByte;

            output[i] = plainByte;
        }

        return output;
    }

    public void Dispose()
    {
        this.transform.Dispose();

        this.aes.Dispose();
    }
}
