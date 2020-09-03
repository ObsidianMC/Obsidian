using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class EntityRelativeMove : Packet
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
        public bool OnGround { get; set; }

        public EntityRelativeMove() : base(0x28) { }
    }
}
