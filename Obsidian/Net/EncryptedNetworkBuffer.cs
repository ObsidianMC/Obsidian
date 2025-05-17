namespace Obsidian.Net;
public sealed class EncryptedNetworkBuffer(byte[] key, byte[] data) : NetworkBuffer(data)
{
    private readonly AesCfbBlockCipher encryptor = new(key);
    private readonly AesCfbBlockCipher decryptor = new(key);

    public EncryptedNetworkBuffer(byte[] key) : this(key, 0) { }
    public EncryptedNetworkBuffer(byte[] key, long capacity) : this(key, new byte[capacity]) { }

    public override void Write(byte[] buffer, int offset, int size)
    {
        var encrypted = this.encryptor.Encrypt(buffer, offset, size);

        base.Write(encrypted);
    }

    public override void WriteByte(byte value) => this.Write([value]);

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var encrypted = this.encryptor.Encrypt(buffer);

        base.Write(encrypted);
    }

    protected override byte[] ReadUntil(int size)
    {
        ValidateOffset();

        if (size == 0)
            return [];

        var decrypted = this.decryptor.Decrypt(this.data, this.offset, size);

        this.offset += decrypted.Length;
        this.BytesPending -= decrypted.Length;

        return decrypted;
    }

    public override void Dispose()
    {
        this.encryptor.Dispose();
        this.decryptor.Dispose();

        base.Dispose();
    }
}
