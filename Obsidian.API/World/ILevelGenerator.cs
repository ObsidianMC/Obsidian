namespace Obsidian.API.World;

/// <summary>
/// Initializes a level's generator and supplies chunks at requested generation stages.
/// </summary>
public interface ILevelGenerator
{
    /// <summary>
    /// Gets the identifier used to register and select this generator.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Initializes the generator for the level it will generate.
    /// </summary>
    public void Init(ILevel level);

    /// <summary>
    /// Generates the chunk at chunk coordinates X and Z to the requested stage, optionally continuing an existing
    /// chunk.
    /// </summary>
    public ValueTask<IChunk> GenerateChunkAsync(int x, int z, IChunk? chunk = null, ChunkGenStage stage = ChunkGenStage.full);

    /// <summary>
    /// Finds where players spawn in a new level, generating the chunks it needs.
    /// </summary>
    /// <returns>The spawn position, or <c>null</c> to let the level search its generated chunks.</returns>
    public ValueTask<VectorD?> FindSpawnPointAsync() => ValueTask.FromResult<VectorD?>(null);

    /// <summary>
    /// Locks a chunk against generation while it's saved, so saves never capture a chunk mid-write.
    /// </summary>
    /// <returns>A handle releasing the lock, or <c>null</c> when the generator doesn't lock chunks.</returns>
    public ValueTask<IDisposable?> LockChunkAsync(int x, int z) => ValueTask.FromResult<IDisposable?>(null);
}
