using System.Formats.Asn1;
using System.Security.Cryptography;

namespace Obsidian.Net;

public sealed class PacketCryptography
{
    private RSA provider;


    internal byte[] VerifyToken { get; set; } = default!;
    internal byte[] PublicKey { get; set; } = default!;

    internal RSAParameters RSAParameters { get; set; } = default!;

    public RSAParameters GenerateKeyPair()
    {
        if (provider is null)
        {
            try
            {
                this.provider = RSA.Create();
                this.provider.KeySize = 1024;

                this.RSAParameters = provider.ExportParameters(true);
            }
            catch
            {
                throw;
            }
        }

        return this.RSAParameters;
    }

    public byte[] Decrypt(byte[] toDecrypt) => this.provider.Decrypt(toDecrypt, RSAEncryptionPadding.Pkcs1);

    public byte[] Encrypt(byte[] toDecrypt) => this.provider.Encrypt(toDecrypt, RSAEncryptionPadding.Pkcs1);

    public (byte[] publicKey, byte[] randomToken) GeneratePublicKeyAndToken()
    {
        var randomToken = RandomNumberGenerator.GetBytes(4);

        this.VerifyToken = randomToken;
        this.PublicKey = this.provider.ExportSubjectPublicKeyInfo();

        return (this.PublicKey, this.VerifyToken);
    }
}
