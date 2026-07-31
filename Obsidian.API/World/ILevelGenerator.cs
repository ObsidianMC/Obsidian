namespace Obsidian.API.World;

public interface ILevelGenerator
{
    public string Id { get; }

    public void Init(ILevel level);

    public ValueTask<IChunk> GenerateChunkAsync(int x, int z, IChunk? chunk = null, ChunkGenStage stage = ChunkGenStage.full);
}
