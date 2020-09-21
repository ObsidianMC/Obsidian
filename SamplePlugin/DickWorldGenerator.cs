using Obsidian.Blocks;
using Obsidian.Util.Registry;
using Obsidian.WorldData;

namespace SamplePlugin
{
    public class DickWorldGenerator : WorldGenerator
    {
        public DickWorldGenerator() : base("dicks")
        {
        }

        public override Chunk GenerateChunk(int x, int z)
        {
            var chunk = new Chunk(x, z);

            void SetBlock2(int x, int y, int z, Block block)
            {
                chunk.SetBlock(x + 1, y + 1, z + 1, block);
                chunk.SetBlock(x + 1, y + 1, z, block);
                chunk.SetBlock(x + 1, y, z + 1, block);
                chunk.SetBlock(x + 1, y, z, block);
                chunk.SetBlock(x, y + 1, z + 1, block);
                chunk.SetBlock(x, y, z + 1, block);
                chunk.SetBlock(x, y + 1, z, block);
                chunk.SetBlock(x, y, z, block);
            }

            for (int x1 = 0; x1 < 8; x1++)
            {
                for (int y = 0; y < 8; y++)
                {
                    for (int z1 = 0; z1 < 8; z1++)
                    {
                        if ((y == 0 && x1 >= 3 && x1 <= 5) || (x1 == 4 && y >= 0 && y <= 2))
                        {
                            SetBlock2(x1 * 2, y * 2, z1 * 2, Registry.GetBlock(Materials.Stone));
                        }
                    }
                }
            }

            return chunk;
        }
    }
}