namespace Obsidian.Blocks
{
    public class BlockState
    {
        public int Id;

        public string UnlocalizedName { get; }

        public Materials Type { get; }

        internal BlockState(int id)
        {
            this.Id = id;
        }

        internal BlockState(string unlocalizedName, int id, Materials type)
        {
            this.Id = id;
            this.UnlocalizedName = unlocalizedName;
            this.Type = type;
        }
    }
}
