using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using System.Buffers;

namespace Obsidian.Net;
public sealed class EncryptedNetworkBuffer : NetworkBuffer
{
    private readonly BufferedBlockCipher encryptCipher;
    private readonly BufferedBlockCipher decryptCipher;

    public EncryptedNetworkBuffer(byte[] key) : this(key, 0) { }
    public EncryptedNetworkBuffer(byte[] key, long capacity) : this(key, new byte[capacity]) { }
    public EncryptedNetworkBuffer(byte[] key, byte[] data) : base(data)
    {
        var keyParam = new KeyParameter(key);
        var ivParam = new ParametersWithIV(keyParam, key, 0, 16);

        encryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
        encryptCipher.Init(true, ivParam);

        decryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
        decryptCipher.Init(false, ivParam);
    }

    public override void Write(byte[] buffer, int offset, int size)
    {
        var output = ArrayPool<byte>.Shared.Rent(encryptCipher.GetOutputSize(size));

        int outLen = encryptCipher.ProcessBytes(buffer, offset, size, output, 0);

        base.Write(output, 0, outLen);

        ArrayPool<byte>.Shared.Return(output);
    }

    public override void WriteByte(byte value)
    {
        Span<byte> single = [value];
        Write(single);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var output = ArrayPool<byte>.Shared.Rent(encryptCipher.GetOutputSize(buffer.Length));
        int outLen = encryptCipher.ProcessBytes(buffer.ToArray(), 0, buffer.Length, output, 0);
        base.Write(output, 0, outLen);

        ArrayPool<byte>.Shared.Return(output);
    }

    protected override byte[] ReadUntil(int size)
    {
        ValidateOffset();
        //Dk how I should do this
        var encrypted = ArrayPool<byte>.Shared.Rent(size);
        Buffer.BlockCopy(this.data, this.offset, encrypted, 0, size);

        var output = new byte[size];
        int outLen = decryptCipher.ProcessBytes(encrypted, 0, size, output, 0);

        ArrayPool<byte>.Shared.Return(encrypted);

        this.offset += size;

        return output;
    }
}
