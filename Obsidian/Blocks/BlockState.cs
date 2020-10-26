namespace Obsidian.Blocks
{
    public class BlockState
    {
        public int Id;

        public string UnlocalizedName { get; }

        public Materials Type { get; }

        public bool IsAir => this.Type == Materials.Air || this.Type == Materials.CaveAir || this.Type == Materials.VoidAir;

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
