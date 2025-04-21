using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Obsidian.API;
public interface INetStream : IDisposable, IAsyncDisposable
{
    public long Size { get; }

    public long Offset { get; }
    public Span<byte> AsSpan();

    public Span<byte> AsSpan(int size);

    public Span<byte> AsSpan(long offset, long? size = null);
}
