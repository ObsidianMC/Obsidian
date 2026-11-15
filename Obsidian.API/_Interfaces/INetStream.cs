namespace Obsidian.API;
/// <summary>
/// A buffer of packet data, read or written in Minecraft's protocol formats.
/// </summary>
public interface INetStream : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the number of bytes of data in the buffer.
    /// </summary>
    public int Size { get; }

    /// <summary>
    /// Gets the position the next read or write starts at.
    /// </summary>
    public int Offset { get; }
    /// <summary>
    /// Gets <see cref="Size"/> bytes of the buffer, starting at <see cref="Offset"/>.
    /// </summary>
    public Span<byte> AsSpan();

    /// <summary>
    /// Gets <paramref name="size"/> bytes of the buffer, starting at <see cref="Offset"/>.
    /// </summary>
    /// <param name="size">The number of bytes.</param>
    public Span<byte> AsSpan(int size);

    /// <summary>
    /// Gets bytes of the buffer, starting at <paramref name="offset"/>.
    /// </summary>
    /// <param name="offset">The position of the first byte.</param>
    /// <param name="size">The number of bytes, or <see langword="null"/> for <see cref="Size"/>.</param>
    public Span<byte> AsSpan(int offset, int? size = null);
}
