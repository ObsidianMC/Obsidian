using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net.Packets.Play.Client
{
    public class EntityTeleport : Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public int EntityId { get; set; }

        [Field(1, true)]
        public Position Position { get; set; }

        [Field(2)]
        public Angle Yaw { get; set; }

        [Field(3)]
        public Angle Pitch { get; set; }

        [Field(4)]
        public bool OnGround { get; set; }

        public EntityTeleport() : base(0x57) { }
    }
}
