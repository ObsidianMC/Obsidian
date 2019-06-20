using Obsidian.BlockData;
using Obsidian.Util;
using Obsidian.World;
using System;
using System.Collections.Generic;
using System.Text;

namespace SamplePlugin
{
    public class DickWorldGenerator : WorldGenerator
    {
        public DickWorldGenerator() : base("dicks")
        {
        }

        public override Chunk GenerateChunk(Chunk chunk)
        {
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

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    for (int z = 0; z < 8; z++)
                    {
                        if ((y == 0 && x >= 3 && x <= 5) || (x == 4 && y >= 0 && y <= 2))
                        {
                            SetBlock2(x * 2, y * 2, z * 2, Blocks.Stone);
                        }
                    }
                }
            }

            return chunk;
        }
    }
}