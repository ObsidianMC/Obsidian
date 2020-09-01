using Obsidian.Items;
using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class SetSlot : Packet
    {
        /// <summary>
        /// 0 for player inventory. -1 For the currently dragged item
        /// </summary>
        [Field(0)]
        public byte WindowId { get; set; } = 0;

        /// <summary>
        /// Can be -1 to set the currently dragged item
        /// </summary>
        [Field(1)]
        public short Slot { get; set; }

        [Field(2)]
        public Slot SlotData { get; set; }

        public SetSlot() : base(0x17) { }
    }
}
