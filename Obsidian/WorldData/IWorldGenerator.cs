namespace Obsidian.WorldData;

public interface IWorldGenerator
{
    public string Id { get; }

    public void Init(IWorld world);

    public ValueTask<IChunk> GenerateChunkAsync(int x, int z, IChunk? chunk = null, ChunkGenStage stage = ChunkGenStage.full);
}
