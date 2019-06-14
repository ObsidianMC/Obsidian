using Obsidian.Net.Packets;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
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
        }

        public AesStream(Stream stream, byte[] key) : base(stream)
        {
            EncryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
            EncryptCipher.Init(true, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

            DecryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
            DecryptCipher.Init(false, new ParametersWithIV(new KeyParameter(key), key, 0, 16));
        }

        public AesStream(byte[] data, byte[] key) : base(data)
        {
            EncryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
            EncryptCipher.Init(true, new ParametersWithIV(new KeyParameter(key), key, 0, 16));

            DecryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 8));
            DecryptCipher.Init(false, new ParametersWithIV(new KeyParameter(key), key, 0, 16));
        }

        public override int ReadByte()
        {
            int value = BaseStream.ReadByte();
            if (value == -1) return value;
            return DecryptCipher.ProcessByte((byte)value)[0];
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int length = BaseStream.Read(buffer, offset, count);
            var decrypted = DecryptCipher.ProcessBytes(buffer, offset, length);

            Array.Copy(decrypted, 0, buffer, offset, decrypted.Length);
            return length;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            int length = await BaseStream.ReadAsync(buffer, offset, count, cancellationToken);
            var decrypted = DecryptCipher.ProcessBytes(buffer, offset, length);

            Array.Copy(decrypted, 0, buffer, offset, decrypted.Length);
            return length;
        }

        public override async Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken = default)
        {
            int length = await BaseStream.ReadAsync(buffer, cancellationToken);
            var decrypted = DecryptCipher.ProcessBytes(buffer, 0, length);

            Array.Copy(decrypted, 0, buffer, 0, decrypted.Length);
            return length;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            var encrypted = EncryptCipher.ProcessBytes(buffer, offset, count);
            await BaseStream.WriteAsync(encrypted, 0, encrypted.Length, cancellationToken);
        }

        public override async Task WriteAsync(byte[] buffer, CancellationToken cancellationToken = default)
        {
            var encrypted = EncryptCipher.ProcessBytes(buffer, 0, buffer.Length);
            await BaseStream.WriteAsync(encrypted, cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var encrypted = EncryptCipher.ProcessBytes(buffer, offset, count);
            BaseStream.Write(encrypted, 0, encrypted.Length);
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
