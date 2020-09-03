using Obsidian.Serializer.Attributes;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net.Packets.Play
{
    public class EntityLookRelativeMove : Packet
    {
        [Field(0, Type = Serializer.Enums.DataType.VarInt)]
        public int EntityId { get; set; }

        [Field(1)]
        public short DeltaX { get; set; }

        [Field(2)]
        public short DeltaY { get; set; }

        [Field(3)]
        public short DeltaZ { get; set; }

        [Field(4)]
        public Angle Yaw { get; set; }

        [Field(5)]
        public Angle Pitch { get; set; }

        [Field(6)]
        public bool OnGround { get; set; }

        public EntityLookRelativeMove() : base(0x29) { }
    }
}
