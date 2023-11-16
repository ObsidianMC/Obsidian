namespace Obsidian.API;
public interface IRegion : IAsyncDisposable
{
    public int X { get; }
    public int Z { get; }

    public int LoadedChunkCount { get; }

    public string RegionFolder { get; }
}
