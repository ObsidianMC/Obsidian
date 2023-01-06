using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.IO;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

using System.IO;

namespace Obsidian.Net;

public sealed class EncryptedMinecraftStream : MinecraftStream
{
    private IBufferedCipher? encryptCipher;
    private IBufferedCipher? decryptCipher;

    public EncryptedMinecraftStream(byte[] key)
    {
        encryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
        encryptCipher.Init(true, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

        decryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
        decryptCipher.Init(false, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

        var oldStream = this.BaseStream;
        this.BaseStream = new CipherStream(oldStream, decryptCipher, encryptCipher);
    }

    public EncryptedMinecraftStream(Stream stream, byte[] key) : base(stream)
    {
        encryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
        encryptCipher.Init(true, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

        decryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
        decryptCipher.Init(false, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

        var oldStream = this.BaseStream;
        this.BaseStream = new CipherStream(oldStream, decryptCipher, encryptCipher);
    }

    public EncryptedMinecraftStream(byte[] data, byte[] key) : base(data)
    {
        encryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
        encryptCipher.Init(true, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

        decryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
        decryptCipher.Init(false, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

        var oldStream = this.BaseStream;
        this.BaseStream = new CipherStream(oldStream, decryptCipher, encryptCipher);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.decryptCipher = null;
            this.encryptCipher = null;
        }

        base.Dispose(disposing);
    }
}
