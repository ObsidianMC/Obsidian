
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.Net
{
    public partial class MinecraftStream : Stream
    {
        private bool disposed;

        public Stream BaseStream { get; set; }

        private MemoryStream debugMemoryStream;

        public override bool CanRead => BaseStream.CanRead;

        public override bool CanSeek => BaseStream.CanSeek;

        public override bool CanWrite => BaseStream.CanWrite;

        public override long Length => BaseStream.Length;

        public override long Position
        {
            get => BaseStream.Position;
            set => BaseStream.Position = value;
        }

        public MinecraftStream(bool debug = false)
        {
            if (debug)
                debugMemoryStream = new MemoryStream();

            BaseStream = new MemoryStream();
        }

        public MinecraftStream(Stream stream, bool debug = false)
        {
            if (debug)
                debugMemoryStream = new MemoryStream();

            BaseStream = stream;
        }

        public MinecraftStream(byte[] data)
        {
            BaseStream = new MemoryStream(data);
        }

        public async Task DumpAsync(bool clear = true)
        {
            if (debugMemoryStream == null)
                throw new Exception("Can't dump a stream who wasn't set to debug.");

            // TODO: Stream the memory stream into a file stream for better performance and stuff :3
            var filePath = Path.Combine(Path.GetTempPath(), "obsidian-" + Path.GetRandomFileName() + ".bin");
            await File.WriteAllBytesAsync(filePath, debugMemoryStream.ToArray());

            if (clear)
            {
                debugMemoryStream.Dispose();
                debugMemoryStream = new MemoryStream();
            }

            await Program.PacketLogger.LogDebugAsync($"Dumped stream to {filePath}");
        }

        public override void Flush() => BaseStream.Flush();

        public override int Read(byte[] buffer, int offset, int count) => BaseStream.Read(buffer, offset, count);

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => await BaseStream.ReadAsync(buffer, offset, count);

        public virtual async Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken = default) => await BaseStream.ReadAsync(buffer, cancellationToken);

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (debugMemoryStream != null)
                await debugMemoryStream.WriteAsync(buffer, offset, count, cancellationToken);

            await BaseStream.WriteAsync(buffer, offset, count);
        }

        public virtual async Task WriteAsync(byte[] buffer, CancellationToken cancellationToken = default)
        {
            if (debugMemoryStream != null)
                await debugMemoryStream.WriteAsync(buffer, cancellationToken);

            await BaseStream.WriteAsync(buffer, cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (debugMemoryStream != null)
                debugMemoryStream.Write(buffer, offset, count);

            BaseStream.Write(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin) => BaseStream.Seek(offset, origin);

        public override void SetLength(long value) => BaseStream.SetLength(value);

        public override void Close() => BaseStream.Close();

        public byte[] ToArray()
        {
            this.Position = 0;
            var buffer = new byte[this.Length];
            for (var totalBytesCopied = 0; totalBytesCopied < this.Length;)
                totalBytesCopied += this.Read(buffer, totalBytesCopied, Convert.ToInt32(this.Length) - totalBytesCopied);
            return buffer;
        }

        protected override void Dispose(bool disposing)
        {
            if (this.disposed)
                return;

            if (disposing)
            {
                this.BaseStream.Dispose();
                this.Lock.Dispose();
            }

            this.disposed = true;
        }
    }
}
