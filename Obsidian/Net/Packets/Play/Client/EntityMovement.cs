using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play.Client
{
    public class EntityMovement : Packet
    {
        [Field(0, Type = Serializer.Enums.DataType.VarInt)]
        public int EntityId { get; set; }

        public EntityMovement() : base(0x2C) { }
    }
}