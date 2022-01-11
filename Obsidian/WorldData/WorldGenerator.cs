namespace Obsidian.WorldData;

public abstract class WorldGenerator
{
    public List<Chunk> Chunks { get; }

    public string Id { get; }

    public WorldGenerator(string id)
    {
        this.Chunks = new List<Chunk>();
        this.Id = id;
    }

    public abstract void Init(string seed);

    public abstract Task<Chunk> GenerateChunkAsync(int x, int z, World world, Chunk? chunk = null);
}
