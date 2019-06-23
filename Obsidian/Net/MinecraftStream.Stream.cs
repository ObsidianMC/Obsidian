using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Obsidian.Util;

namespace Obsidian.Net
{
    public partial class MinecraftStream : Stream
    {
        public MinecraftStream() => BaseStream = new MemoryStream();

        public MinecraftStream(Stream stream) => BaseStream = stream;

        public MinecraftStream(byte[] data) => BaseStream = new MemoryStream(data);

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

        public override int Read(byte[] buffer, int offset, int count) => BaseStream.Read(buffer, offset, count);

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => await BaseStream.ReadAsync(buffer, offset, count);

        public virtual async Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken = default) => await BaseStream.ReadAsync(buffer, cancellationToken);

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => await BaseStream.WriteAsync(buffer, offset, count);

        public virtual async Task WriteAsync(byte[] buffer, CancellationToken cancellationToken = default) => await BaseStream.WriteAsync(buffer, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) => BaseStream.Write(buffer, offset, count);

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

        protected override void Dispose(bool disposing) => BaseStream.Dispose();
    }
}
