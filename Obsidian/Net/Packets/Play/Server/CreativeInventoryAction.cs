using Obsidian.Items;
using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play.Server
{
    public class CreativeInventoryAction : Packet
    {   
        [Field(0)]
        public short ClickedSlot { get; set; }

        [Field(1)]
        public ItemStack ClickedItem { get; set; }

        public CreativeInventoryAction() : base(0x26) { }
    }
}
