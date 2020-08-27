namespace Obsidian.BlockData
{
    public class BlockState
    {
        public int Id;

        public string UnlocalizedName { get; }

        internal BlockState(int id)
        {
            this.Id = id;
        }

        internal BlockState(string unlocalizedName, int id)
        {
            this.Id = id;
            this.UnlocalizedName = unlocalizedName;
        }
    }
}
