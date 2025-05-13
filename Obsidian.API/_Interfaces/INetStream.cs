namespace Obsidian.API;
public interface INetStream : IDisposable, IAsyncDisposable
{
    public int Size { get; }

    public int Offset { get; }
    public Span<byte> AsSpan();

    public Span<byte> AsSpan(int size);

    public Span<byte> AsSpan(int offset, int? size = null);
}
