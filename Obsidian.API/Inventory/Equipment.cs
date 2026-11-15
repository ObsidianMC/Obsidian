namespace Obsidian.API.Inventory;
/// <summary>
/// An item in one of an entity's equipment slots, as entity equipment updates send it.
/// </summary>
public sealed record class Equipment
{
    /// <summary>
    /// Gets the slot the item is in.
    /// </summary>
    public EquipmentSlot Slot { get; init; }

    /// <summary>
    /// Gets the item in the slot.
    /// </summary>
    public ItemStack Item { get; init; }
}

/// <summary>
/// An entity's equipment slots.
/// </summary>
public enum EquipmentSlot : int
{
    /// <summary>
    /// The main hand.
    /// </summary>
    MainHand,
    /// <summary>
    /// The off hand.
    /// </summary>
    OffHand,

    /// <summary>
    /// The feet armour slot.
    /// </summary>
    Boots,
    /// <summary>
    /// The legs armour slot.
    /// </summary>
    Leggings,
    /// <summary>
    /// The chest armour slot.
    /// </summary>
    Chestplate,
    /// <summary>
    /// The head armour slot.
    /// </summary>
    Helmet,
    /// <summary>
    /// An animal's body armour slot, like a horse's or a wolf's.
    /// </summary>
    Body,
    /// <summary>
    /// An animal's saddle slot.
    /// </summary>
    Saddle
}
