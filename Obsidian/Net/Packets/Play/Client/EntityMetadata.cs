using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;

namespace Obsidian.Net.Packets.Play.Client
{
    public class EntityMetadata : Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public int EntityId { get; set; }

        [Field(1, Type = DataType.EntityMetadata)]
        public Entity Entity { get; set; }

        public EntityMetadata() : base(0x44) { }
    }
}
