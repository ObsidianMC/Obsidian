using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using System.Security.Cryptography;

namespace Obsidian.Net;

public class PacketCryptography
{
    private RsaKeyPairGenerator provider;

    private IAsymmetricBlockCipher encryptCipher;
    private IAsymmetricBlockCipher decryptCipher;

    internal byte[] VerifyToken { get; set; }
    internal byte[] PublicKey { get; set; }

    internal AsymmetricCipherKeyPair KeyPair { get; set; }

    public AsymmetricCipherKeyPair GenerateKeyPair()
    {
        if (provider is null)
        {
            try
            {
                provider = new RsaKeyPairGenerator();
                provider.Init(new KeyGenerationParameters(new SecureRandom(), 1024));
                encryptCipher = new Pkcs1Encoding(new RsaEngine());
                decryptCipher = new Pkcs1Encoding(new RsaEngine());
                KeyPair = provider.GenerateKeyPair();

                encryptCipher.Init(true, KeyPair.Public);
                decryptCipher.Init(false, KeyPair.Private);
            }
            catch
            {
                throw;
            }
        }

        return KeyPair;
    }

    public byte[] Decrypt(byte[] toDecrypt) => decryptCipher.ProcessBlock(toDecrypt, 0, decryptCipher.GetInputBlockSize());

    public byte[] Encrypt(byte[] toDecrypt) => encryptCipher.ProcessBlock(toDecrypt, 0, encryptCipher.GetInputBlockSize());

    public (byte[] publicKey, byte[] randomToken) GeneratePublicKeyAndToken()
    {
        var randomToken = new byte[4];
        using var provider = new RNGCryptoServiceProvider();
        provider.GetBytes(randomToken);

        VerifyToken = randomToken;
        PublicKey = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(KeyPair.Public).ToAsn1Object().GetDerEncoded();

        return (PublicKey, VerifyToken);
    }
}
