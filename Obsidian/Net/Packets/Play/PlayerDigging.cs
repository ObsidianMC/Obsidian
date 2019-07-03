using Obsidian.Util;

namespace Obsidian.Net.Packets
{
    public class PlayerDigging : Packet
    {
        [Variable]
        public int Status { get; private set; }

        [Variable]
        public Position Location { get; private set; }

        [Variable]
        public sbyte Face { get; private set; } // This is an enum of what face of the block is being hit

        public PlayerDigging(byte[] packetdata) : base(0x18, packetdata) { }
    }
}
