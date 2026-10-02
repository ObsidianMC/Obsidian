namespace Obsidian.Net;
public sealed class EncryptedNetworkBuffer : NetworkBuffer
{
    private readonly AesCfbBlockCipher encryptor;
    private readonly AesCfbBlockCipher decryptor;
    private readonly bool ownsEncryptor;

    public EncryptedNetworkBuffer(byte[] key, byte[] data) : this(key, data, null) { }

    private EncryptedNetworkBuffer(byte[] key, byte[] data, AesCfbBlockCipher? sharedEncryptor) : base(data)
    {
        encryptor = sharedEncryptor ?? new(key);
        decryptor = new(key);
        ownsEncryptor = sharedEncryptor is null;
    }

    internal EncryptedNetworkBuffer(byte[] key, EncryptedNetworkBuffer outgoingBuffer) : this(key, [], outgoingBuffer.encryptor) { }

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
        if (ownsEncryptor)
            this.encryptor.Dispose();
        this.decryptor.Dispose();

        base.Dispose();
    }
}
