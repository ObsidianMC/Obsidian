using Obsidian.Items;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;

namespace Obsidian.Net.Packets.Play.Client
{
    public class EntityEquipment : Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public int EntityId { get; set; }

        [Field(1, Type = DataType.VarInt)]
        public ESlot Slot { get; set; }

        [Field(2)]
        public Slot Item { get; set; }

        public EntityEquipment() : base(0x47) { }
    }

    public enum ESlot : int
    {
        MainHand,
        OffHand,

        Boots,
        Leggings,
        Chestplate,
        Helmet
    }
}
