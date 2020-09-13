using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play.Client
{
    public class ClientHeldItemChange : Packet
    {
        [Field(0)]
        public byte Slot { get; }

        public ClientHeldItemChange(byte slot) : base(0x40)
        {
            this.Slot = slot;
        }

        public ClientHeldItemChange(byte[] data) : base(0x40, data) { }
    }
}
