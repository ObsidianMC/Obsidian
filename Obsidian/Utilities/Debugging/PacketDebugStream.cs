using System.IO;
using System.Threading;

namespace Obsidian.Utilities.Debugging;

public class PacketDebugStream : Stream
{
    public Stream BaseStream { get; }

    //TODO: Dispose of this when done
    public MemoryStream MemoryStream { get; set; } = new MemoryStream();

    public PacketDebugStream(Stream target)
    {
        BaseStream = target;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        MemoryStream.Write(buffer, offset, count);

        _ = PacketDebug.AppendAsync("", MemoryStream.ToArray());
        MemoryStream = new MemoryStream();

        BaseStream.Write(buffer, offset, count);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await MemoryStream.WriteAsync(buffer, offset, count, cancellationToken);

        await PacketDebug.AppendAsync("", MemoryStream.ToArray());
        MemoryStream = new MemoryStream();

        await BaseStream.WriteAsync(buffer, offset, count, cancellationToken);
    }

    #region Unchanged
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => BaseStream.WriteAsync(buffer, cancellationToken);
    public override void Close() => BaseStream.Close();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => BaseStream.ReadAsync(buffer, offset, count, cancellationToken);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => BaseStream.ReadAsync(buffer, cancellationToken);
    public override Task FlushAsync(CancellationToken cancellationToken) => BaseStream.FlushAsync(cancellationToken);
    public override int ReadByte() => BaseStream.ReadByte();
    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) => BaseStream.CopyToAsync(destination, bufferSize, cancellationToken);
    public override bool CanRead => BaseStream.CanRead;
    public override bool CanSeek => BaseStream.CanSeek;
    public override bool CanWrite => BaseStream.CanWrite;
    public override long Length => BaseStream.Length;
    public override long Position
    {
        get => BaseStream.Position;
        set => BaseStream.Position = value;
    }

    public override void Flush()
    {
        BaseStream.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count) => BaseStream.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => BaseStream.Seek(offset, origin);

    public override void SetLength(long value) => BaseStream.SetLength(value);
    #endregion

}
