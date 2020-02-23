namespace Obsidian.BlockData
{
    public class BlockState
    {
        public int Id;

        public string UnlocalizedName { get; }

        public BlockState(int id)
        {
            this.Id = id;
        }

        public BlockState(string unlocalizedName, int id)
        {
            this.Id = id;
            this.UnlocalizedName = unlocalizedName;
        }
    }
}
