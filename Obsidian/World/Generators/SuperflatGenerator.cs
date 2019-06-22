using Obsidian.BlockData;
using Obsidian.Util.Registry;

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
                    chunk.SetBlock(x, 5, z, BlockRegistry.G(Materials.GrassBlock));
                    chunk.SetBlock(x, 4, z, BlockRegistry.G(Materials.Dirt));
                    chunk.SetBlock(x, 3, z, BlockRegistry.G(Materials.Dirt));
                    chunk.SetBlock(x, 2, z, BlockRegistry.G(Materials.Dirt));
                    chunk.SetBlock(x, 1, z, BlockRegistry.G(Materials.Dirt));
                    chunk.SetBlock(x, 0, z, BlockRegistry.G(Materials.Bedrock));
                }
            }

            return chunk;
        }
    }
}