using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play.Server
{
    public class NameItem : Packet
    {
        [Field(0)]
        public string ItemName { get; set; }

        public NameItem() : base(0x1E) { }
    }
}
