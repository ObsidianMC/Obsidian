using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class EntityEquipment : IClientboundPacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }

    [Field(1), ActualType(typeof(int)), VarLength]
    public ESlot Slot { get; init; }

    [Field(2)]
    public ItemStack Item { get; init; }

    public int Id => 0x50;
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
