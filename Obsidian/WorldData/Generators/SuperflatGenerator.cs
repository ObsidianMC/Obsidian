using Obsidian.Blocks;
using Obsidian.Util.Registry;

namespace Obsidian.WorldData.Generators
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
                    chunk.SetBlock(x, 5, z, Registry.GetBlock(Materials.GrassBlock));
                    chunk.SetBlock(x, 4, z, Registry.GetBlock(Materials.Dirt));
                    chunk.SetBlock(x, 3, z, Registry.GetBlock(Materials.Dirt));
                    chunk.SetBlock(x, 2, z, Registry.GetBlock(Materials.Dirt));
                    chunk.SetBlock(x, 1, z, Registry.GetBlock(Materials.Dirt));
                    chunk.SetBlock(x, 0, z, Registry.GetBlock(Materials.Bedrock));
                }
            }

            return chunk;
        }
    }
}