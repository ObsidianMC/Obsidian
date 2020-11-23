using Obsidian.Blocks;
using Obsidian.Util.Registry;

namespace Obsidian.Items
{
    public class ItemStack : Item
    {
        internal bool Present { get; set; }

        public short Count { get; internal set; }

        public ItemMeta ItemMeta { get; internal set; }

        public ItemStack() : base(Materials.Air) { }

        public ItemStack(short itemId, ItemMeta itemMeta = default) : base(Registry.GetItem(itemId).Type)
        {
            this.Id = itemId;

            this.Count = itemMeta.Count;
        }

        public static ItemStack operator -(ItemStack item, short value)
        {
            if (item.Count <= 0)
                return new ItemStack(0, default);

            item.Count -= value;
            item.ItemMeta.Count -= value;

            return item;
        }
    }
}
