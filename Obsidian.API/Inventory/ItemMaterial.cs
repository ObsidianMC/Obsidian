namespace Obsidian.API.Inventory;

/// <summary>
/// A tool or armour material. The server doesn't read it.
/// </summary>
public enum ItemMaterial
{
    /// <summary>
    /// No material.
    /// </summary>
    None = 1,
    /// <summary>
    /// Wood.
    /// </summary>
    Wood = 2,
    /// <summary>
    /// Stone.
    /// </summary>
    Stone = 4,
    /// <summary>
    /// Iron.
    /// </summary>
    Iron = 6,
    /// <summary>
    /// Diamond.
    /// </summary>
    Diamond = 8,
    /// <summary>
    /// Gold.
    /// </summary>
    Gold = 12,
    /// <summary>
    /// Leather.
    /// </summary>
    Leather = -2,
    /// <summary>
    /// Chainmail.
    /// </summary>
    Chain = -1
}
