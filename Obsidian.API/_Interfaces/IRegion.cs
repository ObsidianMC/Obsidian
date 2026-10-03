using Obsidian.API.ChunkData;
using System.Collections.Concurrent;
using System.Threading;

namespace Obsidian.API;
public interface IRegion : IAsyncDisposable
{
    public int X { get; }
    public int Z { get; }

    public int LoadedChunkCount { get; }

    public string RegionFolder { get; }

    public ConcurrentDictionary<int, IEntity> Entities { get; }

    public void SetChunk(IChunk chunk);

    public ValueTask<IChunk> GetChunkAsync(int x, int z);

    /// <summary>
    /// Gets the chunk at (<paramref name="x"/>, <paramref name="z"/>) in this region, loading it from disk or storing the
    /// one <paramref name="create"/> makes when there's none. Every caller gets the same instance.
    /// </summary>
    public ValueTask<IChunk> GetOrAddChunkAsync(int x, int z, Func<IChunk> create);

    public Task UnloadChunk(int x, int z);

    public Task<bool> InitAsync();

    public void AddBlockUpdate(IBlockUpdate bu);

    public Task BeginTickAsync(CancellationToken cts = default);
    public Task FlushAsync(CancellationToken cts = default);
    public IEnumerable<IChunk> GeneratedChunks();
}
