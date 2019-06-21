using Obsidian.BlockData;

namespace Obsidian.World.Generators
{
    public class SuperflatGenerator : WorldGenerator
    {
        public SuperflatGenerator() : base("superflat")
        {
        }

        public override Chunk GenerateChunk(Chunk chunk)
        {
            for (var x = 0; x < 16; x++)
            {
                for (var z = 0; z < 16; z++)
                {
                    chunk.SetBlock(x, 0, z, Blocks.Cobblestone);
                    chunk.SetBlock(x, 1, z, Blocks.Dirt);
                    chunk.SetBlock(x, 2, z, Blocks.Dirt);
                    chunk.SetBlock(x, 3, z, Blocks.Dirt);
                    chunk.SetBlock(x, 4, z, Blocks.Dirt);
                    chunk.SetBlock(x, 5, z, Blocks.Cobblestone);
                }
            }

            return chunk;
        }
    }
}