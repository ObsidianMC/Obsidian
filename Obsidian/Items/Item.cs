using Obsidian.API;

namespace Obsidian.Items
{
    public class Item
    {
        public string UnlocalizedName { get; set; }

        public Materials Type { get; }

        public int Id { get; set; }
        public ItemNbt Nbt { get; set; }

        public Item(Materials type) => this.Type = type;
    }
}
