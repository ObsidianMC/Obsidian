namespace Obsidian.API.Inventory;
/// <summary>
/// An item's rarity (<c>minecraft:rarity</c>), which colors its name.
/// </summary>
public enum ItemRarity : int
{
    /// <summary>
    /// White name.
    /// </summary>
    Common,
    /// <summary>
    /// Yellow name.
    /// </summary>
    Uncommon,
    /// <summary>
    /// Aqua name.
    /// </summary>
    Rare,
    /// <summary>
    /// Light purple name.
    /// </summary>
    Epic
}
