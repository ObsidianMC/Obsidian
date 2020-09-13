using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play.Server
{
    public class ServerHeldItemChange : Packet
    {
        [Field(0)]
        public short Slot { get; }

        public ServerHeldItemChange(short slot) : base(0x23)
        {
            this.Slot = slot;
        }

        public ServerHeldItemChange(byte[] data) : base(0x23, data) { }
    }
}