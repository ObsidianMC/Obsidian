namespace Obsidian.BlockData
{
    public class BlockState
    {
        public int Id = 0;

        public string UnlocalizedName { get; }

        public BlockState(int id)
        {
            this.Id = id;
        }

        public BlockState(string unlocalizedName, int Id)
        {
            this.Id = Id;
            this.UnlocalizedName = unlocalizedName;
        }
    }
}
