namespace Obsidian.API;
public interface INetStream : IDisposable, IAsyncDisposable
{
    public long Size { get; }

    public long Offset { get; }
}
