namespace Obsidian.API.Inventory;
/// <summary>
/// An item's rarity (<c>minecraft:rarity</c>), which colors its name.
/// </summary>
public enum ItemRarity : int
{
    /// <summary>
    /// The default rarity, given to every new stack: a white name.
    /// </summary>
    Common,
    /// <summary>
    /// Shown with a yellow name.
    /// </summary>
    Uncommon,
    /// <summary>
    /// Shown with an aqua name.
    /// </summary>
    Rare,
    /// <summary>
    /// Shown with a light purple name.
    /// </summary>
    Epic
}
