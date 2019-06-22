namespace Obsidian.BlockData
{
    public class Block : BlockState
    {
        public byte Metadata { get; set; }

        internal Block(string name, int id) : base(name, id)
        {
        }

        

        public void Set(Block block)
        {
            this.Id = block.Id;
        }
    }
}
