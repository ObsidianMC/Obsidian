namespace Obsidian.Items
{
    public class ItemStack
    {
        public ItemStack(short itemId, byte itemCount, byte metadata)
        {
            ItemId = itemId;
            ItemCount = itemCount;
            Nbt = 0;
            MetaData = metadata;
        }

        public ItemStack(Item item, byte itemCount)
        {
            ItemId = (short)item.Id;
            ItemCount = itemCount;
            MetaData = item.Metadata;
        }

        public short ItemId { get; set; }
        public byte ItemCount { get; set; }
        public byte MetaData { get; set; }
        public byte Nbt { get; private set; }
    }
}
