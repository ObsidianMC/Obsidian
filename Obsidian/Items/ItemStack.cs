namespace Obsidian.Items
{
    public class ItemStack : Item
    {
        public int Count { get; set; }

        public ItemStack(int itemId, int itemCount)
        {
            this.Id = itemId;
            this.Count = itemCount;
        }
    }
}
