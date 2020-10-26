using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play.Server
{
    public class PickItem : Packet 
    {
        [Field(0)]
        public int SlotToUse { get; set; }

        public PickItem() : base(0x18) { }
    }
}
