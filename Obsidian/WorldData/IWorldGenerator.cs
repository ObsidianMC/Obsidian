namespace Obsidian.WorldData;

public interface IWorldGenerator
{
    public string Id { get; }

    public void Init(string seed);

    public Task<Chunk> GenerateChunkAsync(int x, int z, World world, Chunk? chunk = null);
}
