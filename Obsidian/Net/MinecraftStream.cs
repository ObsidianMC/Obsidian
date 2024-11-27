using System.IO;
using System.Threading;

namespace Obsidian.Net;

public partial class MinecraftStream : Stream
{
    private bool disposed;

    public Stream BaseStream { get; set; }

    public SemaphoreSlim Lock { get; } = new SemaphoreSlim(1, 1);

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
            this.debugMemoryStream = new MemoryStream();

        this.BaseStream = new MemoryStream();
    }

    public MinecraftStream(Stream stream, bool debug = false)
    {
        if (debug)
            this.debugMemoryStream = new MemoryStream();

        this.BaseStream = stream;
    }

    public MinecraftStream(byte[] data)
    {
        this.BaseStream = new MemoryStream(data);
    }

    // Unused
    public async Task DumpAsync(bool clear = true, IPacket packet = null)
    {
        if (this.debugMemoryStream == null)
            throw new Exception("Can't dump a stream who wasn't set to debug.");

        // TODO: Stream the memory stream into a file stream for better performance and stuff :3
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"obsidian"));

        var filePath = Path.Combine(Path.GetTempPath(), $"obsidian/obsidian-{(packet != null ? packet.GetType().Name : "")}-" + Path.GetRandomFileName() + ".bin");
        await File.WriteAllBytesAsync(filePath, this.debugMemoryStream.ToArray());

        if (clear)
            await ClearDebug();

        //Globals.PacketLogger.LogDebug("Dumped stream to {FilePath}", filePath);
    }

    // unused
    public async Task DumpAsync(bool clear = true, string name = "")
    {
        if (this.debugMemoryStream == null)
            throw new Exception("Can't dump a stream who wasn't set to debug.");

        // TODO: Stream the memory stream into a file stream for better performance and stuff :3
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"obsidian"));

        var filePath = Path.Combine(Path.GetTempPath(), $"obsidian/obsidian-{name}-" + Path.GetRandomFileName() + ".bin");
        await File.WriteAllBytesAsync(filePath, this.debugMemoryStream.ToArray());

        if (clear)
            await ClearDebug();

        //Globals.PacketLogger.LogDebug("Dumped stream to {FilePath}", filePath);
    }

    public Task ClearDebug()
    {
        this.debugMemoryStream.Dispose();
        this.debugMemoryStream = new MemoryStream();

        return Task.CompletedTask;
    }

    public override void Flush() => this.BaseStream.Flush();

    public override int Read(byte[] buffer, int offset, int count) => this.BaseStream.Read(buffer, offset, count);

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        try
        {
            var read = await BaseStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

            return read;
        }
        catch (Exception)
        {
            return 0;
        }//TODO better handling of this
    }

    public virtual async Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            var read = await this.BaseStream.ReadAsync(buffer, cancellationToken);

            return read;
        }
        catch (Exception)
        {
            return 0;
        }//TODO better handling of this
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (this.debugMemoryStream != null)
            await debugMemoryStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);

        await BaseStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public virtual async Task WriteAsync(byte[] buffer, CancellationToken cancellationToken = default)
    {
        if (this.debugMemoryStream != null)
            await this.debugMemoryStream.WriteAsync(buffer, cancellationToken);

        await this.BaseStream.WriteAsync(buffer, cancellationToken);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (this.debugMemoryStream != null)
            this.debugMemoryStream.Write(buffer, offset, count);

        this.BaseStream.Write(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin) => this.BaseStream.Seek(offset, origin);

    public override void SetLength(long value) => this.BaseStream.SetLength(value);

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
