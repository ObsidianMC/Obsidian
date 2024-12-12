namespace Obsidian.API.Inventory;
public sealed record class Equipment
{
    public EquipmentSlot Slot { get; init; }

    public ItemStack Item { get; init; }
}

public enum EquipmentSlot : int
{
    MainHand,
    OffHand,

    Boots,
    Leggings,
    Chestplate,
    Helmet
}
