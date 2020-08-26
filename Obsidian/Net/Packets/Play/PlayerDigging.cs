using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net.Packets.Play
{
    public class PlayerDigging : Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public int Status { get; private set; }

        [Field(1)]
        public Position Location { get; private set; }

        [Field(2)]
        public sbyte Face { get; private set; } // This is an enum of what face of the block is being hit

        public PlayerDigging() : base(0x18) { }

        public PlayerDigging(byte[] packetdata) : base(0x18, packetdata) { }
    }
}