using System;
using System.Numerics;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Obsidian.Net
{
    public static class PacketCryptography
    {
        private static RsaKeyPairGenerator provider;

        public static byte[] VerifyToken { get; set; }

        public static AsymmetricCipherKeyPair KeyPair { get; set; }

        private static IAsymmetricBlockCipher EncryptCipher { get; set; }
        private static IAsymmetricBlockCipher DecryptCipher { get; set; }

        //https://gist.github.com/ammaraskar/7b4a3f73bee9dc4136539644a0f27e63
        public static string MinecraftShaDigest(byte[] data)
        {
            var hash = new SHA1Managed().ComputeHash(data);
            // Reverse the bytes since BigInteger uses little endian
            Array.Reverse(hash);

            var b = new BigInteger(hash);
            // very annoyingly, BigInteger in C# tries to be smart and puts in
            // a leading 0 when formatting as a hex number to allow roundtripping 
            // of negative numbers, thus we have to trim it off.
            if (b < 0)
            {
                // toss in a negative sign if the interpreted number is negative
                return "-" + (-b).ToString("x").TrimStart('0');
            }
            else
            {
                return b.ToString("x").TrimStart('0');
            }
        }

        public static AsymmetricCipherKeyPair GenerateKeyPair()
        {
            if (provider is null)
            {
                try
                {
                    provider = new RsaKeyPairGenerator();
                    provider.Init(new KeyGenerationParameters(new SecureRandom(), 1024));
                    EncryptCipher = new Pkcs1Encoding(new RsaEngine());
                    DecryptCipher = new Pkcs1Encoding(new RsaEngine());
                    KeyPair = provider.GenerateKeyPair();

                    EncryptCipher.Init(true, KeyPair.Public);
                    DecryptCipher.Init(false, KeyPair.Private);
                }
                catch
                {
                    throw;
                }
            }

            return KeyPair;
        }

        public static byte[] Decrypt(byte[] toDecrypt)
        {
            return DecryptCipher.ProcessBlock(toDecrypt, 0, DecryptCipher.GetInputBlockSize());
        }

        public static byte[] Encrypt(byte[] toDecrypt)
        {
            return EncryptCipher.ProcessBlock(toDecrypt, 0, EncryptCipher.GetInputBlockSize());
        }

        public static byte[] GetRandomToken()
        {
            var token = new byte[4];
            var provider = new RNGCryptoServiceProvider();
            
            provider.GetBytes(token);
            VerifyToken = token;
            return token;
        }

        public static byte[] PublicKeyToAsn()
        {
            return SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(KeyPair.Public).ToAsn1Object().GetDerEncoded();
        }
    }
}
