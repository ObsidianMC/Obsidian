using Obsidian.Serializer.Attributes;
using Obsidian.API;

namespace Obsidian.Net.Packets.Play.Server
{
    public class PlayerPosition : Packet
    {
        [Field(0, true)]
        public Position Position { get; set; }

        [Field(1)]

        public bool OnGround { get; private set; }

        public PlayerPosition() : base(0x12) { }

        public PlayerPosition(byte[] data) : base(0x12, data) { }

        public PlayerPosition(Position pos, bool onground) : base(0x12)
        {
            this.Position = pos;
            this.OnGround = onground;
        }
    }
}