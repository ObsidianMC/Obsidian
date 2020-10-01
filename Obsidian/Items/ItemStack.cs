using Obsidian.Util.Registry;

namespace Obsidian.Items
{
    public class ItemStack : Item
    {
        internal bool Present { get; set; }

        public int Count { get; set; }

        public ItemStack() { }

        public ItemStack(int itemId, int itemCount)
        {
            this.Id = itemId;
            this.Count = itemCount;
            this.Type = Registry.GetItem(itemId).Type;
        }
    }
}
