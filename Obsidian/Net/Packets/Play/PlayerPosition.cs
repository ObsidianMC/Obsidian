using Obsidian.Serializer.Attributes;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net.Packets.Play
{
    public class PlayerPosition : Packet
    {
        [PacketOrder(0, true)]
        public Position Position { get; set; }

        [PacketOrder(1)]

        public bool OnGround { get; private set; }

        public PlayerPosition() : base(0x10) { }

        public PlayerPosition(byte[] data) : base(0x10, data) { }

        public PlayerPosition(Position pos, bool onground) : base(0x10)
        {
            this.Position = pos;
            this.OnGround = onground;
        }
    }
}