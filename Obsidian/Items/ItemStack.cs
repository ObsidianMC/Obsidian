using Obsidian.API;
using Obsidian.Util.Registry;

namespace Obsidian.Items
{
    public class ItemStack : Item
    {
        public bool Present { get; set; }

        public int Count { get; set; }

        public ItemStack() : base(Materials.Air) { }

        public ItemStack(int itemId, int itemCount) : base(Registry.GetItem(itemId).Type)
        {
            this.Id = itemId;
            this.Count = itemCount;
        }

        public static ItemStack operator -(ItemStack item, int value)
        {
            if (item.Count <= 0)
                return new ItemStack(0, 0);

            item.Count -= value;
            return item;
        }
    }
}
