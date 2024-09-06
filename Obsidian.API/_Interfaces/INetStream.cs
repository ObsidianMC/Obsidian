namespace Obsidian.API;
public interface INetStream : IDisposable, IAsyncDisposable
{
    public long Length { get; }

    public long Position { get; set; }
}
