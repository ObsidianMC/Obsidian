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
            var index = this.Blocks.FindIndex(x => x.Position == position);
            this.Blocks[index].Set(block);
        }

        public void SetBlock(int x, int y, int z, Block block)
        {
            var index = this.Blocks.FindIndex(b => b.Position.Match(x, y, z));

            this.Blocks[index].Set(block);
        }
    }
}
