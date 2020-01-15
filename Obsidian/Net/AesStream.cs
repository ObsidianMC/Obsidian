using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.IO;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;


namespace Obsidian.Net
{
    public class AesStream : MinecraftStream
    {
        private IBufferedCipher EncryptCipher { get; set; }
        private IBufferedCipher DecryptCipher { get; set; }

        public AesStream(byte[] key)
        {
            EncryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
            EncryptCipher.Init(true, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

            DecryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
            DecryptCipher.Init(false, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

            semaphore = new Semaphore(1, 1);

            var oldStream = this.BaseStream;
            this.BaseStream = new CipherStream(oldStream, DecryptCipher, EncryptCipher);
        }

        public AesStream(Stream stream, byte[] key) : base(stream)
        {
            EncryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
            EncryptCipher.Init(true, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

            DecryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
            DecryptCipher.Init(false, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

            semaphore = new Semaphore(1, 1);

            var oldStream = this.BaseStream;
            this.BaseStream = new CipherStream(oldStream, DecryptCipher, EncryptCipher);
        }

        public AesStream(byte[] data, byte[] key) : base(data)
        {
            EncryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
            EncryptCipher.Init(true, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

            DecryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
            DecryptCipher.Init(false, new ParametersWithIV(new KeyParameter(key), key, 0, 16));
            semaphore = new Semaphore(1, 1);

            var oldStream = this.BaseStream;
            this.BaseStream = new CipherStream(oldStream, DecryptCipher, EncryptCipher);
        }

        public override int ReadByte()
        {
            int value = BaseStream.ReadByte();
            if (value == -1) return value;

            return value;
            //return DecryptCipher.ProcessByte((byte)value)[0];
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int length = BaseStream.Read(buffer, offset, count);
            //var decrypted = DecryptCipher.ProcessBytes(buffer, offset, length);

            //Array.Copy(decrypted, 0, buffer, offset, decrypted.Length);
            return length;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            int length = await BaseStream.ReadAsync(buffer, offset, count, cancellationToken);
            //var decrypted = DecryptCipher.ProcessBytes(buffer, offset, length);

            //Array.Copy(decrypted, 0, buffer, offset, decrypted.Length);
            return length;
        }

        public override async Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken = default)
        {
            int length = await BaseStream.ReadAsync(buffer, cancellationToken);
            //var decrypted = DecryptCipher.ProcessBytes(buffer, 0, length);

            //Array.Copy(decrypted, 0, buffer, 0, decrypted.Length);
            return length;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            //var encrypted = EncryptCipher.ProcessBytes(buffer, offset, count);
            await BaseStream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override async Task WriteAsync(byte[] buffer, CancellationToken cancellationToken = default)
        {
            //var encrypted = EncryptCipher.ProcessBytes(buffer, 0, buffer.Length);
            await BaseStream.WriteAsync(buffer, cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            // var encrypted = EncryptCipher.ProcessBytes(buffer, offset, count);
            BaseStream.Write(buffer, offset, count);
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
    }
}
