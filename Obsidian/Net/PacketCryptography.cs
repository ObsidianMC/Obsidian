using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using System.Security.Cryptography;

namespace Obsidian.Net;

public sealed class PacketCryptography
{
    private RsaKeyPairGenerator provider = default!;

    private IAsymmetricBlockCipher encryptCipher = default!;
    private IAsymmetricBlockCipher decryptCipher = default!;

    internal byte[] VerifyToken { get; set; } = default!;
    internal byte[] PublicKey { get; set; } = default!;

    internal AsymmetricCipherKeyPair KeyPair { get; set; } = default!;

    public AsymmetricCipherKeyPair GenerateKeyPair()
    {
        if (provider is null)
        {
            try
            {
                this.provider = new RsaKeyPairGenerator();
                this.provider.Init(new KeyGenerationParameters(new SecureRandom(), 1024));
                this.encryptCipher = new Pkcs1Encoding(new RsaEngine());
                this.decryptCipher = new Pkcs1Encoding(new RsaEngine());
                this.KeyPair = provider.GenerateKeyPair();

                this.encryptCipher.Init(true, KeyPair.Public);
                this.decryptCipher.Init(false, KeyPair.Private);
            }
            catch
            {
                throw;
            }
        }

        return this.KeyPair;
    }

    public byte[] Decrypt(byte[] toDecrypt) => this.decryptCipher.ProcessBlock(toDecrypt, 0, this.decryptCipher.GetInputBlockSize());

    public byte[] Encrypt(byte[] toDecrypt) => this.encryptCipher.ProcessBlock(toDecrypt, 0, this.encryptCipher.GetInputBlockSize());

    public (byte[] publicKey, byte[] randomToken) GeneratePublicKeyAndToken()
    {
        var randomToken = RandomNumberGenerator.GetBytes(4);

        this.VerifyToken = randomToken;
        this.PublicKey = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(this.KeyPair.Public).ToAsn1Object().GetDerEncoded();

        return (this.PublicKey, this.VerifyToken);
    }
}
