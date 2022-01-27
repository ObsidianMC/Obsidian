using Microsoft.Extensions.Logging;
using Obsidian.Net.Packets;
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

    public async Task DumpAsync(bool clear = true, IPacket packet = null)
    {
        if (debugMemoryStream == null)
            throw new Exception("Can't dump a stream who wasn't set to debug.");

        // TODO: Stream the memory stream into a file stream for better performance and stuff :3
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"obsidian"));

        var filePath = Path.Combine(Path.GetTempPath(), $"obsidian/obsidian-{(packet != null ? packet.GetType().Name : "")}-" + Path.GetRandomFileName() + ".bin");
        await File.WriteAllBytesAsync(filePath, debugMemoryStream.ToArray());

        if (clear)
            await ClearDebug();

        Globals.PacketLogger.LogDebug($"Dumped stream to {filePath}");
    }

    public async Task DumpAsync(bool clear = true, string name = "")
    {
        if (debugMemoryStream == null)
            throw new Exception("Can't dump a stream who wasn't set to debug.");

        // TODO: Stream the memory stream into a file stream for better performance and stuff :3
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"obsidian"));

        var filePath = Path.Combine(Path.GetTempPath(), $"obsidian/obsidian-{name}-" + Path.GetRandomFileName() + ".bin");
        await File.WriteAllBytesAsync(filePath, debugMemoryStream.ToArray());

        if (clear)
            await ClearDebug();

        Globals.PacketLogger.LogDebug($"Dumped stream to {filePath}");
    }

    public Task ClearDebug()
    {
        debugMemoryStream.Dispose();
        debugMemoryStream = new MemoryStream();

        return Task.CompletedTask;
    }

    public override void Flush() => BaseStream.Flush();

    public override int Read(byte[] buffer, int offset, int count) => BaseStream.Read(buffer, offset, count);

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
            var read = await BaseStream.ReadAsync(buffer, cancellationToken);

            return read;
        }
        catch (Exception)
        {
            return 0;
        }//TODO better handling of this
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (debugMemoryStream != null)
            await debugMemoryStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);

        await BaseStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
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
        Position = 0;
        var buffer = new byte[Length];
        for (var totalBytesCopied = 0; totalBytesCopied < Length;)
            totalBytesCopied += Read(buffer, totalBytesCopied, Convert.ToInt32(Length) - totalBytesCopied);
        return buffer;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposed)
            return;

        if (disposing)
        {
            BaseStream.Dispose();
            Lock.Dispose();
        }

        disposed = true;
    }
}
