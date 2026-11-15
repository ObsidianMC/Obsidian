namespace Obsidian.API.Inventory;

/// <summary>
/// A broad kind of item. The server doesn't read it.
/// </summary>
public enum ItemType
{
    /// <summary>
    /// Swords.
    /// </summary>
    Sword,
    /// <summary>
    /// Shovels.
    /// </summary>
    Shovel,
    /// <summary>
    /// Pickaxes.
    /// </summary>
    PickAxe,
    /// <summary>
    /// Axes.
    /// </summary>
    Axe,
    /// <summary>
    /// Items that aren't one of the other kinds.
    /// </summary>
    Item,

    /// <summary>
    /// Helmets, worn in the head slot.
    /// </summary>
    Helmet,
    /// <summary>
    /// Chestplates, worn in the chest slot.
    /// </summary>
    Chestplate,
    /// <summary>
    /// Leggings, worn in the legs slot.
    /// </summary>
    Leggings,
    /// <summary>
    /// Boots, worn in the feet slot.
    /// </summary>
    Boots
}
