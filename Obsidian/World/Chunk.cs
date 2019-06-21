using Obsidian.BlockData;
using Obsidian.Util;
using System.Collections.Generic;

namespace Obsidian.World
{
    public class Chunk
    {
        public int X { get; }
        public int Z { get; }

        public bool Loaded { get; }

        public List<Block> Blocks { get; }

        public Chunk(int x, int z)
        {
            this.X = x;
            this.Z = z;

            this.Blocks = new List<Block>();
            for (int chunkX = 0; chunkX < 16; chunkX++)
            {
                for (int chunkY = 0; chunkY < 16; chunkY++)
                {
                    for (int chunkZ = 0; chunkZ < 16; chunkZ++)
                    {
                        var air = BlockData.Blocks.Air;
                        air.Position = new Position(chunkX, chunkY, chunkZ);
                        this.Blocks.Add(air);
                    }
                }
            }
        }

        public Block GetBlock(Position position)
        {
            var index = this.Blocks.FindIndex(x => x.Position == position);

            return this.Blocks[index];
        }

        public Block GetBlock(int x, int y, int z)
        {
            var index = this.Blocks.FindIndex(b => b.Position.Match(x, y, z));

            return this.Blocks[index];
        }

        public void SetBlock(Position position, Block block)
        {
            try
            {
                var index = this.Blocks.FindIndex(b => b.Position == position);

                this.Blocks[index].Set(block);
            }
            catch
            {
                block.Position = position;
                this.Blocks[this.Blocks.Count + 1].Set(block);
            }
        }

        public void SetBlock(int x, int y, int z, Block block)
        {
            try
            {
                var index = this.Blocks.FindIndex(b => b.Position.Match(x, y, z));

                this.Blocks[index].Set(block);
            }
            catch
            {
                block.Position = new Position(x, y, z);
                this.Blocks[this.Blocks.Count + 1].Set(block);
            }

        }
    }
}