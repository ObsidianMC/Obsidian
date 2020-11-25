using Obsidian.Blocks;
using Obsidian.Util.Registry;

namespace Obsidian.Items
{
    public class ItemStack : Item
    {
        public readonly static ItemStack Air = new ItemStack(0, 0, default);

        internal bool Present { get; set; }

        public short Count { get; internal set; }

        public ItemMeta ItemMeta { get; internal set; }

        public ItemStack() : base("minecraft:air", Materials.Air) { }

        public ItemStack(short itemId, short count, ItemMeta? itemMeta = null) : base(Registry.GetItem(itemId).UnlocalizedName, Registry.GetItem(itemId).Type)
        {
            this.Id = itemId;
            this.Count = count;

            if (itemMeta != null)
                this.ItemMeta = itemMeta.Value;
        }

        public static ItemStack operator -(ItemStack item, short value)
        {
            if (item.Count <= 0)
                return new ItemStack(0, 0, default);

            item.Count -= value;

            return item;
        }
    }
}
