using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Obsidian.Utilities;

internal sealed class ReadOnlyStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;

    public override long Length => length;
    public override long Position { get => index; set => Math.Clamp(value, 0, length - 1); }

    private readonly byte[] data;
    private readonly long offset;
    private readonly long length;
    private long index;

    public ReadOnlyStream(ReadOnlyMemory<byte> memory)
    {
        if (!MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment))
        {
            throw new ArgumentException($"'{nameof(memory)}' must be backed up by an array.");
        }

        if (segment.Array is null)
        {
            throw new ArgumentException($"'{nameof(memory)}' contains a null reference.");
        }

        data = segment.Array;
        offset = segment.Offset;
        length = segment.Count;
        index = 0;
    }

    public override int Read(Span<byte> buffer)
    {
        int bytesRead = (int)Math.Min(length - index, buffer.Length);
        GetSpan(bytesRead).CopyTo(buffer);
        index += bytesRead;
        return bytesRead;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    private Span<byte> GetSpan(int size)
    {
        ref byte first = ref MemoryMarshal.GetArrayDataReference(data);
        first = ref Unsafe.Add(ref first, (int)(offset + index));
        return MemoryMarshal.CreateSpan(ref first, size);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => index + offset,
            SeekOrigin.End => length + offset,
            _ => throw new ArgumentException($"Invalid {nameof(origin)}")
        };
        return Position;
    }

    public override void Flush() => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
