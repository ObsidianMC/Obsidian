using Obsidian.Utilities.Registry;

namespace Obsidian.WorldData.Generators;

public class SuperflatGenerator : WorldGenerator
{
    public SuperflatGenerator() : base("superflat") { }

    public override Chunk GenerateChunk(int x, int z, World world, Chunk chunk = null)
    {
        chunk = new Chunk(x, z);
        for (var x1 = 0; x1 < 16; x1++)
        {
            for (var z1 = 0; z1 < 16; z1++)
            {
                chunk.SetBlock(x1, -60, z1, Registry.GetBlock(Material.GrassBlock));
                chunk.SetBlock(x1, -61, z1, Registry.GetBlock(Material.Dirt));
                chunk.SetBlock(x1, -62, z1, Registry.GetBlock(Material.Dirt));
                chunk.SetBlock(x1, -63, z1, Registry.GetBlock(Material.Dirt));
                chunk.SetBlock(x1, -64, z1, Registry.GetBlock(Material.Bedrock));
            }
        }

        return chunk;
    }
}
