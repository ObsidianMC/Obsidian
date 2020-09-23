using Obsidian.Blocks;

namespace Obsidian.Items
{
    public class Item
    {
        public string Name { get; set; }

        public Materials Type { get; set; }

        public int Id { get; set; }
        public ItemNbt Nbt { get; set; }
    }
}
