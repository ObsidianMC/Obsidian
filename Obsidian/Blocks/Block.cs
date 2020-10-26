using System.Collections.Generic;

namespace Obsidian.Blocks
{
    public class Block : BlockState
    {
        public byte Metadata { get; set; }

        public List<BlockState> States { get; private set; } = new List<BlockState>();

        internal Block(string name, int id, Materials type) : base(name, id, type)
        {
        }

        public void Set(Block block)
        {
            this.Id = block.Id;
        }
    }
}
