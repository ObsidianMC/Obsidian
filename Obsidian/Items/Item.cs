using Obsidian.Blocks;

namespace Obsidian.Items
{
    public class Item
    {
        public string UnlocalizedName { get; internal set; }

        public Materials Type { get; }

        public int Id { get; set; }

        public Item(Materials type) => this.Type = type;

        public Item(string unlocalizedName, Materials type) : this(type) { this.UnlocalizedName = unlocalizedName; }
    }
}
