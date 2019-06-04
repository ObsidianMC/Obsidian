using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.Util
{
    public partial class AesStream : System.IO.Stream
    {
        private Chilkat.Crypt2 Crypt { get; }
        private Chilkat.Crypt2 Decrypt { get; }

        private PaddedBufferedBlockCipher encryptCipher { get; set; }
        private PaddedBufferedBlockCipher decryptCipher { get; set; }

        internal byte[] Key { get; set; }

        public AesStream(byte[] key)
        {
            BaseStream = new MemoryStream();
            Key = key;

            this.Crypt = new Chilkat.Crypt2()
            {
                CryptAlgorithm = "aes",
                CipherMode = "cfb",
                KeyLength = 128,
                PaddingScheme = 0,
                SecretKey = key,
                IV = key
            };

            this.Decrypt = new Chilkat.Crypt2()
            {
                CryptAlgorithm = "aes",
                CipherMode = "cfb",
                KeyLength = 128,
                PaddingScheme = 0,
                SecretKey = key,
                IV = key
            };

            encryptCipher = new PaddedBufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
            encryptCipher.Init(true, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

            decryptCipher = new PaddedBufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
            decryptCipher.Init(false, new ParametersWithIV(new KeyParameter(key), key, 0, 16));
        }

        public AesStream(Stream stream, byte[] key)
        {
            BaseStream = stream;
            Key = key;

            this.Crypt = new Chilkat.Crypt2()
            {
                CryptAlgorithm = "aes",
                CipherMode = "cfb",
                KeyLength = 128,
                PaddingScheme = 0,
                SecretKey = key,
                IV = key
            };

            //RsaKeyPairGenerator rkpg = new RsaKeyPairGenerator();

           
            this.Decrypt = new Chilkat.Crypt2()
            {
                CryptAlgorithm = "aes",
                CipherMode = "cfb",
                KeyLength = 128,
                PaddingScheme = 0,
                SecretKey = key,
                IV = key
            };

            encryptCipher = new PaddedBufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
            encryptCipher.Init(true, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

            decryptCipher = new PaddedBufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
            decryptCipher.Init(false, new ParametersWithIV(new KeyParameter(key), key, 0, 16));
        }

        public AesStream(byte[] data, byte[] key)
        {
            BaseStream = new MemoryStream(data);
            Key = key;

            this.Crypt = new Chilkat.Crypt2()
            {
                CryptAlgorithm = "aes",
                CipherMode = "cfb",
                KeyLength = 128,
                PaddingScheme = 0,
                SecretKey = key,
                IV = key
            };

            this.Decrypt = new Chilkat.Crypt2()
            {
                CryptAlgorithm = "aes",
                CipherMode = "cfb",
                KeyLength = 128,
                PaddingScheme = 0,
                SecretKey = key,
                IV = key
            };

            encryptCipher = new PaddedBufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
            encryptCipher.Init(true, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

            decryptCipher = new PaddedBufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
            decryptCipher.Init(false, new ParametersWithIV(new KeyParameter(key), key, 0, 16));
        }

        public Stream BaseStream { get; set; }

        public override bool CanRead
        {
            get { return BaseStream.CanRead; }
        }

        public override bool CanSeek
        {
            get { return BaseStream.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return BaseStream.CanWrite; }
        }

        public override long Length
        {
            get { return BaseStream.Length; }
        }

        public override long Position
        {
            get { return BaseStream.Position; }
            set { BaseStream.Position = value; }
        }

        public override void Flush()
        {
            BaseStream.Flush();
        }

        public override int ReadByte()
        {
            int value = BaseStream.ReadByte();
            if (value == -1) return value;
            return decryptCipher.ProcessByte((byte)value)[0];
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int length = BaseStream.Read(buffer, offset, count);
            var decrypted = this.Decrypt.DecryptBytes(buffer);
            //var decrypted = decryptCipher.ProcessBytes(buffer, offset, length);
            Array.Copy(decrypted, 0, buffer, offset, decrypted.Length);
            return length;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            int length = await BaseStream.ReadAsync(buffer, offset, count, cancellationToken);
            var decrypted = this.Decrypt.DecryptBytes(buffer);
            //var decrypted = decryptCipher.ProcessBytes(buffer, offset, length);
            Array.Copy(decrypted, 0, buffer, offset, decrypted.Length);
            return length;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int length = await BaseStream.ReadAsync(buffer, cancellationToken);
            //var decrypted = decryptCipher.ProcessBytes(buffer.ToArray(), 0, length);
            var decrypted = this.Decrypt.DecryptBytes(buffer.ToArray());
            Array.Copy(decrypted, 0, buffer.ToArray(), 0, decrypted.Length);
            return length;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            var encrypted = this.Crypt.EncryptBytes(buffer);
            // var encrypted = encryptCipher.ProcessBytes(buffer, offset, count);
            await BaseStream.WriteAsync(encrypted, offset, encrypted.Length, cancellationToken);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var encrypted = this.Crypt.EncryptBytes(buffer.ToArray());
            //var encrypted = encryptCipher.ProcessBytes(buffer.ToArray(), 0, buffer.Length);
            await BaseStream.WriteAsync(encrypted, cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var encrypted = this.Crypt.EncryptBytes(buffer);
            //var encrypted = encryptCipher.ProcessBytes(buffer, offset, count);
            BaseStream.Write(encrypted, offset, encrypted.Length);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return BaseStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            BaseStream.SetLength(value);
        }

        public override void Close()
        {
            BaseStream.Close();
        }

        public byte[] ToArray()
        {
            this.Position = 0;
            byte[] buffer = new byte[this.Length];
            for (int totalBytesCopied = 0; totalBytesCopied < this.Length;)
                totalBytesCopied += this.Read(buffer, totalBytesCopied, Convert.ToInt32(this.Length) - totalBytesCopied);
            return buffer;
        }
    }
}
