using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play.Server
{
    public class ServerHeldItemChange : Packet
    {
        [Field(0)]
        public short Slot { get; set; }

        public ServerHeldItemChange() : base(0x23) { }
    }
}