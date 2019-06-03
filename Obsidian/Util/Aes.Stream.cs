using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.Util
{
    public partial class AesStream : Stream
    {
        private BufferedBlockCipher encryptCipher { get; set; }
        private BufferedBlockCipher decryptCipher { get; set; }
        internal byte[] Key { get; set; }

        public AesStream(byte[] key)
        {
            BaseStream = new MemoryStream();
            Key = key;
            encryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesFastEngine(), 8));
            encryptCipher.Init(true, new ParametersWithIV(new KeyParameter(Key), Key, 0, 16));

            decryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesFastEngine(), 8));
            decryptCipher.Init(false, new ParametersWithIV(new KeyParameter(Key), Key, 0, 16));
        }

        public AesStream(Stream stream, byte[] key)
        {
            BaseStream = stream;
            Key = key;
            encryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesFastEngine(), 8));
            encryptCipher.Init(true, new ParametersWithIV(new KeyParameter(Key), Key, 0, 16));

            decryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesFastEngine(), 8));
            decryptCipher.Init(false, new ParametersWithIV(new KeyParameter(Key), Key, 0, 16));
        }

        public AesStream(byte[] data, byte[] key)
        {
            BaseStream = new MemoryStream(data);
            Key = key;
            encryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesFastEngine(), 8));
            encryptCipher.Init(true, new ParametersWithIV(new KeyParameter(Key), Key, 0, 16));

            decryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesFastEngine(), 8));
            decryptCipher.Init(false, new ParametersWithIV(new KeyParameter(Key), Key, 0, 16));
        }

        public Stream BaseStream { get; set; }

        public override bool CanRead => BaseStream.CanRead;

        public override bool CanSeek => BaseStream.CanSeek;

        public override bool CanWrite => BaseStream.CanWrite;

        public override long Length => BaseStream.Length;

        public override long Position
        {
            get => BaseStream.Position;
            set => BaseStream.Position = value;
        }

        public override void Flush() => BaseStream.Flush();

        public override int ReadByte()
        {
            int value = BaseStream.ReadByte();
            if (value == -1) return value;
            return decryptCipher.ProcessByte((byte)value)[0];
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int length = BaseStream.Read(buffer, offset, count);
            var decrypted = decryptCipher.ProcessBytes(buffer, offset, length);
            Array.Copy(decrypted, 0, buffer, offset, decrypted.Length);
            return length;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            int length = await BaseStream.ReadAsync(buffer, offset, count, cancellationToken);
            var decrypted = decryptCipher.ProcessBytes(buffer, offset, length);
            Array.Copy(decrypted, 0, buffer, offset, decrypted.Length);
            return length;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int length = await BaseStream.ReadAsync(buffer, cancellationToken);
            var decrypted = decryptCipher.ProcessBytes(buffer.ToArray(), 0, length);
            Array.Copy(decrypted, 0, buffer.ToArray(), 0, decrypted.Length);
            return length;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            var encrypted = encryptCipher.ProcessBytes(buffer, offset, count);
            await BaseStream.WriteAsync(encrypted, offset, encrypted.Length, cancellationToken);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var encrypted = encryptCipher.ProcessBytes(buffer.ToArray(), 0, buffer.Length);
            await BaseStream.WriteAsync(encrypted, cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var encrypted = encryptCipher.ProcessBytes(buffer, offset, count);
            BaseStream.Write(encrypted, offset, encrypted.Length);
        }

        public override long Seek(long offset, SeekOrigin origin) => BaseStream.Seek(offset, origin);

        public override void SetLength(long value) => BaseStream.SetLength(value);

        public override void Close() => BaseStream.Close();

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
