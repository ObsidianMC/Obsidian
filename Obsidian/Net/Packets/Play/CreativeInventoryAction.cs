using Obsidian.Items;
using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class CreativeInventoryAction : Packet
    {   
        [Field(0)]
        public short ClickedSlot { get; set; }

        [Field(1)]
        public Slot ClickedItem { get; set; }

        public CreativeInventoryAction() : base(0x24) { }
    }
}
