using Obsidian.Nbt;
using System.Buffers;
using System.IO;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// A write-only stream into an array rented from the shared pool, grown by doubling. What was written is
/// <see cref="Written"/>. Disposing the stream only ends writing (writers such as <see cref="NbtWriterStream"/> dispose
/// the stream they write to), so the bytes stay readable until <see cref="Return"/> gives the array back to the pool.
/// </summary>
internal sealed class PooledBufferStream(int initialCapacity) : Stream
{
    private byte[] buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);

    private int length;

    private bool disposed;

    private bool returned;

    public ReadOnlyMemory<byte> Written => this.buffer.AsMemory(0, this.length);

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => this.length;

    public override long Position
    {
        get => this.length;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => this.Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> source)
    {
        this.EnsureCapacity(this.length + source.Length);

        source.CopyTo(this.buffer.AsSpan(this.length));
        this.length += source.Length;
    }

    public override void WriteByte(byte value) => this.Write([value]);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        this.Write(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
    {
        this.Write(source.Span);
        return ValueTask.CompletedTask;
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>Gives the array back to the pool; nothing may use <see cref="Written"/> after.</summary>
    public void Return()
    {
        if (this.returned)
            return;

        this.returned = true;
        this.disposed = true;
        ArrayPool<byte>.Shared.Return(this.buffer);
        this.buffer = [];
        this.length = 0;
    }

    protected override void Dispose(bool disposing)
    {
        this.disposed = true;
        base.Dispose(disposing);
    }

    private void EnsureCapacity(int capacity)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        if (capacity <= this.buffer.Length)
            return;

        var larger = ArrayPool<byte>.Shared.Rent(Math.Max(capacity, this.buffer.Length * 2));
        this.buffer.AsSpan(0, this.length).CopyTo(larger);
        ArrayPool<byte>.Shared.Return(this.buffer);
        this.buffer = larger;
    }
}
