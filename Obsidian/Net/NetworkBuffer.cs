using System.Diagnostics;

namespace Obsidian.Net;
/// <summary>
/// Dynamic byte buffer
/// </summary>
public partial class NetworkBuffer
{
    protected byte[] data;
    protected long size;
    protected long offset;

    /// <summary>
    /// Is the buffer empty?
    /// </summary>
    public bool IsEmpty => (data == null) || (size == 0);
    /// <summary>
    /// Bytes memory buffer
    /// </summary>
    public byte[] Data => data;
    /// <summary>
    /// Bytes memory buffer capacity
    /// </summary>
    public long Capacity => data.Length;
    /// <summary>
    /// Bytes memory buffer size
    /// </summary>
    public long Size => size;
    /// <summary>
    /// Bytes memory buffer offset
    /// </summary>
    public long Offset => offset;

    /// <summary>
    /// Buffer indexer operator
    /// </summary>
    public byte this[long index] => data[index];

    public NetworkBuffer() : this([], 0, 0) { }
    public NetworkBuffer(long capacity) : this(new byte[capacity], 0, 0) { }
    public NetworkBuffer(byte[] data) : this(data, data.LongLength, 0) { }

    private NetworkBuffer(byte[] buffer, long size, long offset)
    {
        this.data = buffer;
        this.size = size;
        this.offset = offset;
    }

    #region Memory buffer methods

    /// <summary>
    /// Get a span of bytes from the current buffer
    /// </summary>
    public Span<byte> AsSpan()
    {
        return new Span<byte>(data, (int)offset, (int)size);
    }

    /// <summary>
    /// Clear the current buffer and its offset
    /// </summary>
    public void Clear()
    {
        size = 0;
        offset = 0;
    }

    /// <summary>
    /// Remove the buffer of the given offset and size
    /// </summary>
    public void Remove(long offset, long size)
    {
        Debug.Assert(((offset + size) <= Size), "Invalid offset & size!");
        if ((offset + size) > Size)
            throw new ArgumentException("Invalid offset & size!", nameof(offset));

        Array.Copy(data, offset + size, data, offset, this.size - size - offset);
        this.size -= size;
        if (this.offset >= (offset + size))
            this.offset -= size;
        else if (this.offset >= offset)
        {
            this.offset -= this.offset - offset;
            if (this.offset > Size)
                this.offset = Size;
        }
    }

    /// <summary>
    /// Reserve the buffer of the given capacity
    /// </summary>
    public void Reserve(long capacity)
    {
        if (capacity < this.Capacity)
            return;

        Array.Resize(ref this.data, (int)capacity);

        this.size = capacity;
        if (this.offset > this.size)
            this.offset = this.size;
    }
    #endregion

    #region Buffer I/O methods

    /// <summary>
    /// Append the single byte
    /// </summary>
    /// <param name="value">Byte value to append</param>
    public virtual void WriteByte(byte value)
    {
        Reserve(size + 1);
        data[size] = value;
        size += 1;
    }

    /// <summary>
    /// Append the given buffer
    /// </summary>
    /// <param name="buffer">Buffer to append</param>
    public virtual void Write(byte[] buffer)
    {
        Reserve(size + buffer.Length);
        Array.Copy(buffer, 0, data, size, buffer.Length);
        size += buffer.Length;
    }

    /// <summary>
    /// Append the given buffer fragment
    /// </summary>
    /// <param name="buffer">Buffer to append</param>
    /// <param name="offset">Buffer offset</param>
    /// <param name="size">Buffer size</param>
    public virtual void Write(byte[] buffer, int offset, int size)
    {
        Reserve(this.size + size);
        Array.Copy(buffer, offset, data, this.size, size);
        this.size += size;
    }

    /// <summary>
    /// Append the given span of bytes
    /// </summary>
    /// <param name="buffer">Buffer to append as a span of bytes</param>
    public virtual void Write(ReadOnlySpan<byte> buffer)
    {
        Reserve(size + buffer.Length);
        buffer.CopyTo(new Span<byte>(data, (int)size, buffer.Length));
        size += buffer.Length;
    }

    /// <summary>
    /// Append the given buffer
    /// </summary>
    /// <param name="buffer">Buffer to append</param>
    public virtual void Write(INetStream buffer) => Write(buffer.AsSpan());

    #endregion

    public void Dispose() { this.Clear(); }

    public ValueTask DisposeAsync()
    {
        this.Dispose();

        return default;
    }
}
