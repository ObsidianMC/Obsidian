using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using System;
using System.IO;

namespace Obsidian.Util
{
    public class AesStream : Stream
    {
        private BufferedBlockCipher encryptCipher { get; set; }
        private BufferedBlockCipher decryptCipher { get; set; }
        internal byte[] Key { get; set; }

        public AesStream(Stream stream, byte[] key)
        {
            BaseStream = stream;
            Key = key;
            encryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesFastEngine(), 8));
            encryptCipher.Init(true, new ParametersWithIV(
                new KeyParameter(key), key, 0, 16));
            decryptCipher = new BufferedBlockCipher(new CfbBlockCipher(new AesFastEngine(), 8));
            decryptCipher.Init(false, new ParametersWithIV(
                new KeyParameter(key), key, 0, 16));
        }

        public Stream BaseStream { get; set; }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }

        public override long Length
        {
            get { throw new NotSupportedException(); }
        }

        public override long Position
        {
            get { return 0; } // hack for libnbt
            set { throw new NotSupportedException(); }
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
            var decrypted = decryptCipher.ProcessBytes(buffer, offset, length);
            Array.Copy(decrypted, 0, buffer, offset, decrypted.Length);
            return length;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var encrypted = encryptCipher.ProcessBytes(buffer, offset, count);
            BaseStream.Write(encrypted, 0, encrypted.Length);
        }

        public override void Close()
        {
            BaseStream.Close();
        }
    }
}
