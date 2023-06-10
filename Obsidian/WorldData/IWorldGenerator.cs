namespace Obsidian.WorldData;

public interface IWorldGenerator
{
    public string Id { get; }

    public void Init(IWorld world);

    public Task<Chunk> GenerateChunkAsync(int x, int z, Chunk? chunk = null, ChunkStatus minlevel = ChunkStatus.full);
}
