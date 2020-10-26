using Obsidian.Serializer.Attributes;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net.Packets.Play.Server
{
    public class ServerPlayerPositionLook : Packet
    {
        [Field(0, true)]
        public Position Position { get; set; }

        [Field(1)]
        public float Pitch { get; set; }

        [Field(2)]
        public float Yaw { get; set; }

        [Field(3)]
        public bool OnGround { get; set; }

        public ServerPlayerPositionLook() : base(0x34) { }
    }
}
