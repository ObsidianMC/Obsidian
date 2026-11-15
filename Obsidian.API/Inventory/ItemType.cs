namespace Obsidian.API.Inventory;

/// <summary>
/// A broad kind of item. The server doesn't read it.
/// </summary>
public enum ItemType
{
    /// <summary>
    /// A sword.
    /// </summary>
    Sword,
    /// <summary>
    /// A shovel.
    /// </summary>
    Shovel,
    /// <summary>
    /// A pickaxe.
    /// </summary>
    PickAxe,
    /// <summary>
    /// An axe.
    /// </summary>
    Axe,
    /// <summary>
    /// Any other item.
    /// </summary>
    Item,

    /// <summary>
    /// A helmet.
    /// </summary>
    Helmet,
    /// <summary>
    /// A chestplate.
    /// </summary>
    Chestplate,
    /// <summary>
    /// Leggings.
    /// </summary>
    Leggings,
    /// <summary>
    /// Boots.
    /// </summary>
    Boots
}
