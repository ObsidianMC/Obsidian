using Obsidian.API;

namespace Obsidian.Items
{
    public struct Item
    {
        public string UnlocalizedName { get; }

        public Materials Type { get; }

        public short Id { get; internal set; }

        public Item(int id, string unlocalizedName, Materials type)
        {
            this.Id = (short)id;
            this.UnlocalizedName = unlocalizedName;
            this.Type = type;
        }
    }
}
