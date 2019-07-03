using Obsidian.Util;

namespace Obsidian.Net.Packets
{
    public class PingPong : Packet
    {
        [Variable(VariableType.Long)]
        public long Payload;

        public PingPong(byte[] data) : base(0x01, data) { }
    }
}