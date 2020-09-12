using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class EntityPacket : Packet
    {
        [Field(0, Type = Serializer.Enums.DataType.VarInt)]
        public int EntityId { get; set; }

        public EntityPacket() : base(0x2C) { }
    }
}