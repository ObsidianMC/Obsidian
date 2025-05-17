using System.Buffers;
using System.Security.Cryptography;

namespace Obsidian.Net;
public sealed class AesCfbChopped : IDisposable
{
    private readonly Aes aes;
    private readonly ICryptoTransform encryptor;
    private readonly ICryptoTransform decryptor;
    private readonly byte[] iv;

    private const int BlockSize = 16;

    public AesCfbChopped(byte[] key, byte[] iv)
    {
        this.iv = iv;

        this.aes = Aes.Create();
        this.aes.Mode = CipherMode.ECB;
        this.aes.Padding = PaddingMode.None;
        this.aes.FeedbackSize = 8;
        this.aes.Key = key;
        this.aes.IV = iv;

        this.encryptor = aes.CreateEncryptor();
        this.decryptor = aes.CreateDecryptor();
    }

    public byte[] Encrypt(Span<byte> buffer)
    {
        var block = ArrayPool<byte>.Shared.Rent(BlockSize);

        var copied = new byte[buffer.Length];
        for (int i = 0; i < copied.Length; i++)
        {
            this.encryptor.TransformBlock(this.iv, 0, 16, block, 0);
            byte cipherByte = (byte)(buffer[i] ^ block[0]);

            // Update IV
            Buffer.BlockCopy(this.iv, 1, this.iv, 0, 15);
            this.iv[15] = cipherByte;

            copied[i] = cipherByte;
        }

        ArrayPool<byte>.Shared.Return(block);

        return copied;
    }

    public byte[] Decrypt(Span<byte> buffer, int offset, int count)
    {
        var block = ArrayPool<byte>.Shared.Rent(BlockSize);

        var copied = new byte[count];
        for (int i = 0; i < count; i++)
        {
            var cipherByte = buffer[offset + i];

            this.decryptor.TransformBlock(this.iv, 0, 16, block, 0);
            var decryptedByte = (byte)(cipherByte ^ block[0]);

            // Shift IV left and append ciphertext byte
            Buffer.BlockCopy(this.iv, 1, this.iv, 0, 15);

            this.iv[15] = cipherByte;
            copied[i] = decryptedByte;
         }

        ArrayPool<byte>.Shared.Return(block);

        return copied;
    }

    public void Dispose()
    {
        encryptor.Dispose();
        aes.Dispose();
    }
}
