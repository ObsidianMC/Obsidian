using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class EntityEquipment : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        [Field(1), ActualType(typeof(int)), VarLength]
        public ESlot Slot { get; set; }

        [Field(2)]
        public ItemStack Item { get; set; }

        public int Id => 0x47;
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
