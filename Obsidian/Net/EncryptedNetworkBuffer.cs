using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace Obsidian.Net;
public sealed class EncryptedNetworkBuffer : NetworkBuffer
{
    private readonly BufferedBlockCipher encryptCipher;
    private readonly BufferedBlockCipher decryptCipher;

    public EncryptedNetworkBuffer(byte[] key) : this(key, 0) { }
    public EncryptedNetworkBuffer(byte[] key, long capacity) : this(key, new byte[capacity]) { }
    public EncryptedNetworkBuffer(byte[] key, byte[] data) : base(data)
    {
        encryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
        encryptCipher.Init(true, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

        decryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
        decryptCipher.Init(false, new ParametersWithIV(new KeyParameter(key), key, 0, 16));
    }

    public override void Write(byte[] buffer, int offset, int size)
    {
        var span = new ReadOnlySpan<byte>(buffer, offset, size);
        var encrypted = ProcessCipher(encryptCipher, span);
        Write(encrypted);
    }

    protected override byte[] ReadUntil(int size)
    {
        ValidateOffset();
        var encrypted = base.ReadUntil(size);
        return ProcessCipher(decryptCipher, encrypted);
    }

    private static byte[] ProcessCipher(BufferedBlockCipher cipher, ReadOnlySpan<byte> input)
    {
        var inputBuffer = input.ToArray(); // still need to copy since BouncyCastle requires array
        var output = new byte[cipher.GetOutputSize(inputBuffer.Length)];
        var length = cipher.ProcessBytes(inputBuffer, 0, inputBuffer.Length, output, 0);
        length += cipher.DoFinal(output, length);

        if (length < output.Length)
            Array.Resize(ref output, length);

        return output;
    }
}
