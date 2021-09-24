using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class ClientHeldItemChange : IClientboundPacket
    {
        [Field(0)]
        public byte Slot { get; }

        public int Id => 0x48;

        public ClientHeldItemChange(byte slot)
        {
            Slot = slot;
        }
    }
}
