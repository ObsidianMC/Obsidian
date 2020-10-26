using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;

namespace Obsidian.Net.Packets.Play.Client
{
    public class CollectItem : Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public int CollectedEntityId { get; set; }

        [Field(1, Type = DataType.VarInt)]
        public int CollectorEntityId { get; set; }

        [Field(2, Type = DataType.VarInt)]
        public int PickupItemCount { get; set; }

        public CollectItem() : base(0x55) { }
    }
}
