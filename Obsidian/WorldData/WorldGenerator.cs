namespace Obsidian.WorldData;

public abstract class WorldGenerator
{
    public List<Chunk> Chunks { get; }

    public string Id { get; }

    public WorldGenerator()
    {
        var attributes = this.GetType().GetCustomAttributes(typeof(WorldGeneratorAttribute), false);
        if (attributes.Length < 1)
            throw new Exception($"World generator with type {this.GetType().Name} has no ID!");

        this.Chunks = new List<Chunk>();
        this.Id = ((WorldGeneratorAttribute)(this.GetType().GetCustomAttributes(typeof(WorldGeneratorAttribute), false)[0])).Id;
    }

    public abstract Task<Chunk> GenerateChunkAsync(int x, int z, World world, Chunk chunk = null);
}
