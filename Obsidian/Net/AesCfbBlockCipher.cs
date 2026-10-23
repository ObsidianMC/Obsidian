using System.Security.Cryptography;

namespace Obsidian.Net;

/// <summary>
/// One direction of the protocol's stream cipher: AES in 8-bit CFB mode, with the shared secret as both key and IV.
/// Each call continues the stream where the last one left off.
/// </summary>
public sealed class AesCfbBlockCipher : IDisposable
{
    private const int BlockSize = 16;
    private const int FeedbackBits = 8;

    private readonly Aes aes;

    // CFB8's shift register: the last 16 bytes of ciphertext, which seed the next call.
    private readonly byte[] iv;

    public AesCfbBlockCipher(byte[] key)
    {
        this.iv = (byte[])key.Clone();

        this.aes = Aes.Create();
        this.aes.Key = key;
    }

    /// <summary>
    /// Encrypts <paramref name="data"/> where it is.
    /// </summary>
    public void EncryptInPlace(Span<byte> data)
    {
        if (data.IsEmpty)
            return;

        this.aes.EncryptCfb(data, this.iv, data, PaddingMode.None, FeedbackBits);
        this.Advance(data);
    }

    public Span<byte> Encrypt(ReadOnlySpan<byte> buffer)
    {
        var output = buffer.ToArray();
        this.EncryptInPlace(output);
        return output;
    }

    public Span<byte> Encrypt(byte[] buffer, int offset, int count) => this.Encrypt(buffer.AsSpan(offset, count));

    public byte[] Decrypt(ReadOnlySpan<byte> buffer, int offset, int count)
    {
        var ciphertext = buffer.Slice(offset, count);
        var output = new byte[count];
        if (count == 0)
            return output;

        this.aes.DecryptCfb(ciphertext, this.iv, output, PaddingMode.None, FeedbackBits);
        this.Advance(ciphertext);

        return output;
    }

    // Shifts the ciphertext just produced or consumed into the register, as encrypting it a byte at a time would.
    private void Advance(ReadOnlySpan<byte> ciphertext)
    {
        if (ciphertext.Length >= BlockSize)
        {
            ciphertext[^BlockSize..].CopyTo(this.iv);
            return;
        }

        this.iv.AsSpan(ciphertext.Length).CopyTo(this.iv);
        ciphertext.CopyTo(this.iv.AsSpan(BlockSize - ciphertext.Length));
    }

    public void Dispose() => this.aes.Dispose();
}
