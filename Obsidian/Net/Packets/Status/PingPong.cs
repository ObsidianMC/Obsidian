using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Status
{
    public class PingPong : Packet
    {
        [PacketOrder(0)]
        public long Payload;

        public PingPong(byte[] data) : base(0x01, data) { }
    }
}