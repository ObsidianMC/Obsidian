using System.Diagnostics;

namespace Obsidian.Net;
/// <summary>
/// Dynamic byte buffer
/// </summary>
public partial class NetworkBuffer
{
    private const int DefaultInitialCapacity = 64;

    protected byte[] data;
    protected int size;
    protected int offset;

    /// <summary>
    /// Is the buffer empty?
    /// </summary>
    public bool IsEmpty => size == 0;
    /// <summary>
    /// Bytes memory buffer
    /// </summary>
    public byte[] Data => data;
    /// <summary>
    /// Bytes memory buffer capacity
    /// </summary>
    public int Capacity => data.Length;
    /// <summary>
    /// Bytes memory buffer size
    /// </summary>
    public int Size => size;
    /// <summary>
    /// Bytes memory buffer offset
    /// </summary>
    public int Offset { get => this.offset; internal set => this.offset = value; }

    public int BytesPending { get; internal set; }

    /// <summary>
    /// Buffer indexer operator
    /// </summary>
    public byte this[long index] => data[index];

    public NetworkBuffer() : this(0) { }
    public NetworkBuffer(long capacity) : this(new byte[capacity], 0, 0) { }
    public NetworkBuffer(byte[] data) : this(data, data.Length, 0) { }

    private NetworkBuffer(byte[] buffer, int size, int offset)
    {
        this.data = buffer;
        this.size = size;
        this.offset = offset;
    }

    #region Memory buffer methods

    /// <summary>
    /// Get a span of bytes from the current buffer
    /// </summary>
    public Span<byte> AsSpan() => new(data, offset, size);

    /// <summary>
    /// Get a span of bytes from the current buffer with the specified size.
    /// </summary>
    public Span<byte> AsSpan(int size) => new(data, offset, size);

    public Span<byte> AsSpan(int offset, int? size = null) => new(data, offset, size.HasValue ? size.Value : this.size);

    /// <summary>
    /// Clear the current buffer and its offset
    /// </summary>
    public void Clear()
    {
        size = 0;
        offset = 0;
    }

    public void Reset() => this.offset = 0;

    /// <summary>
    /// Reserve the buffer of the given capacity
    /// </summary>
    public void Reserve(int capacity)
    {
        Debug.Assert(capacity >= 0);
        var required = offset + capacity;

        if (required > data.LongLength)
        {
            var newCapacity = Math.Max(required, data.Length * 2);
            Array.Resize(ref data, newCapacity);
        }

        size = Math.Max(size, required);
    }
    #endregion

    #region Buffer I/O methods

    /// <summary>
    /// Append the single byte
    /// </summary>
    /// <param name="value">Byte value to append</param>
    public virtual void WriteByte(byte value)
    {
        Reserve(ByteSize);
        data[this.offset] = value;
        this.offset += ByteSize;
    }

    /// <summary>
    /// Append the given buffer
    /// </summary>
    /// <param name="buffer">Buffer to append</param>
    public virtual void Write(byte[] buffer) => this.Write(buffer.AsSpan());

    /// <summary>
    /// Append the given buffer fragment
    /// </summary>
    /// <param name="buffer">Buffer to append</param>
    /// <param name="offset">Buffer offset</param>
    /// <param name="size">Buffer size</param>
    public virtual void Write(byte[] buffer, int offset, int size)
    {
        Reserve(size);
        Array.Copy(buffer, offset, data, this.offset, size);
        this.offset += size;
    }

    /// <summary>
    /// Append the given span of bytes
    /// </summary>
    /// <param name="buffer">Buffer to append as a span of bytes</param>
    public virtual void Write(ReadOnlySpan<byte> buffer)
    {
        Reserve(buffer.Length);
        buffer.CopyTo(new Span<byte>(data, this.offset, buffer.Length));
        this.offset += buffer.Length;
    }

    /// <summary>
    /// Append the given buffer
    /// </summary>
    /// <param name="buffer">Buffer to append</param>
    public virtual void Write(INetStream buffer) => Write(buffer.AsSpan(0, buffer.Size));

    #endregion

    public virtual void Dispose()
    {
        this.Clear();

        GC.SuppressFinalize(this);
    }

    public virtual ValueTask DisposeAsync()
    {
        this.Dispose();

        return default;
    }
}
