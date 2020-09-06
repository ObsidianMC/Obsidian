namespace Obsidian.Items
{
    public class ItemStack : Item
    {
        public byte ItemCount { get; set; }
        public byte MetaData { get; set; }
        public byte Nbt { get; private set; }

        public ItemStack(int itemId, byte itemCount, byte metadata)
        {
            this.Id = itemId;
            this.ItemCount = itemCount;
            this.Nbt = 0;
            this.MetaData = metadata;
        }
    }
}
