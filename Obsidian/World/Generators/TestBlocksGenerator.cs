using Obsidian.BlockData;
using Obsidian.Util.Registry;

namespace Obsidian.World.Generators
{
    public class TestBlocksGenerator : WorldGenerator
    {
        public TestBlocksGenerator() : base("test")
        {
        }

        public override Chunk GenerateChunk(Chunk chunk)
        {
            int countX = 0;
            int countZ = 0;

            foreach (var block in BlockRegistry.BlockStates.Values)
            {
                if (block is BlockAir || block is BlockBed)
                    continue;

                if (countX == 16)
                {
                    countX = 0;
                    countZ++;
                }

                chunk.SetBlock(countX, 1, countZ, block);

                countX++;
            }

            this.Chunks.Add(chunk);

            return chunk;
        }
    }
}
