using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net.Packets.Play.Client
{
    public class EntityVelocity :  Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public int EntityId { get; set; }

        [Field(1)]
        public Velocity Velocity { get; set; }

        public EntityVelocity() : base(0x46) { }
    }
}
