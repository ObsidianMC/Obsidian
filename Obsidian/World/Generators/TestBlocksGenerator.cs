using Obsidian.BlockData;

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

            foreach (var block in Blocks.BLOCK_STATES)
            {
                if (block is BlockAir || block is BlockBed)
                    continue;

                if (countX == 15)
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
