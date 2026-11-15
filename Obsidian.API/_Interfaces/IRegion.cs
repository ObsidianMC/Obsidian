using Obsidian.API.ChunkData;
using System.Collections.Concurrent;
using System.Threading;

namespace Obsidian.API;
/// <summary>
/// A region of 32 by 32 chunks in a level, stored in its own region files.
/// </summary>
public interface IRegion : IAsyncDisposable
{
    /// <summary>
    /// Gets the region's x coordinate, in regions (a chunk's x coordinate divided by 32, rounded down).
    /// </summary>
    public int X { get; }
    /// <summary>
    /// Gets the region's z coordinate, in regions (a chunk's z coordinate divided by 32, rounded down).
    /// </summary>
    public int Z { get; }

    /// <summary>
    /// Gets the number of the region's loaded chunks that are generated.
    /// </summary>
    public int LoadedChunkCount { get; }

    /// <summary>
    /// Gets the folder that holds the region's files.
    /// </summary>
    public string RegionFolder { get; }

    /// <summary>
    /// The entities in the region's chunks, by entity id. Read-only: the level adds and removes entities (see
    /// <see cref="World.ILevel.TryAddEntity"/> and <see cref="World.ILevel.DestroyEntityAsync"/>), keeping its other
    /// records of them in step.
    /// </summary>
    public IReadOnlyDictionary<int, IEntity> Entities { get; }

    /// <summary>
    /// Stores <paramref name="chunk"/> in the region at its position, replacing the chunk loaded there. Does nothing if
    /// it is <see langword="null"/>.
    /// </summary>
    /// <param name="chunk">The chunk to store.</param>
    public void SetChunk(IChunk chunk);

    /// <summary>
    /// Gets the chunk at (<paramref name="x"/>, <paramref name="z"/>) in this region, loading it from disk if needed.
    /// </summary>
    /// <param name="x">The chunk's x coordinate within the region, from 0 to 31.</param>
    /// <param name="z">The chunk's z coordinate within the region, from 0 to 31.</param>
    /// <returns>The chunk, or <see langword="null"/> if it is neither loaded nor saved.</returns>
    public ValueTask<IChunk> GetChunkAsync(int x, int z);

    /// <summary>
    /// Gets the chunk at (<paramref name="x"/>, <paramref name="z"/>) in this region, loading it from disk or storing the
    /// one <paramref name="create"/> makes when there's none. Every caller gets the same instance.
    /// </summary>
    public ValueTask<IChunk> GetOrAddChunkAsync(int x, int z, Func<IChunk> create);

    /// <summary>
    /// Saves the chunk at (<paramref name="x"/>, <paramref name="z"/>) in this region and removes it from memory.
    /// </summary>
    /// <param name="x">The chunk's x coordinate within the region, from 0 to 31.</param>
    /// <param name="z">The chunk's z coordinate within the region, from 0 to 31.</param>
    public Task UnloadChunk(int x, int z);

    /// <summary>
    /// Waits for the region's files to be opened.
    /// </summary>
    /// <returns><see langword="true"/> once the region is ready.</returns>
    public Task<bool> InitAsync();

    /// <summary>
    /// Ticks every entity in the region once, in order of entity id, then hands entities that moved into another loaded
    /// region to it.
    /// </summary>
    /// <param name="cts">Stops the tick between entities.</param>
    public Task BeginTickAsync(CancellationToken cts = default);
    /// <summary>
    /// Writes the chunks that changed since their last save to the region's files and waits for them to reach the disk.
    /// </summary>
    /// <param name="cts">Not used.</param>
    public Task FlushAsync(CancellationToken cts = default);
    /// <summary>
    /// Enumerates the region's loaded chunks that are generated.
    /// </summary>
    public IEnumerable<IChunk> GeneratedChunks();
}
