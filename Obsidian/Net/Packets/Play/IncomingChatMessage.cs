using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class IncomingChatMessage : Packet
    {
        [PacketOrder(0)]
        public string Message { get; private set; }

        public IncomingChatMessage() : base(0x02) { }

        public IncomingChatMessage(byte[] data) : base(0x02, data) { }
    }
}