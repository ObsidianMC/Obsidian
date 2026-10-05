namespace Obsidian.API.World;

public interface ILevelGenerator
{
    public string Id { get; }

    public void Init(ILevel level);

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
