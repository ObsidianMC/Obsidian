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
    public ValueTask<VectorF?> FindSpawnPointAsync() => ValueTask.FromResult<VectorF?>(null);
}
