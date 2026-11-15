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
    /// The material of wooden tools.
    /// </summary>
    Wood = 2,
    /// <summary>
    /// The material of stone tools.
    /// </summary>
    Stone = 4,
    /// <summary>
    /// The material of iron tools and armour.
    /// </summary>
    Iron = 6,
    /// <summary>
    /// The material of diamond tools and armour.
    /// </summary>
    Diamond = 8,
    /// <summary>
    /// The material of golden tools and armour.
    /// </summary>
    Gold = 12,
    /// <summary>
    /// The material of leather armour.
    /// </summary>
    Leather = -2,
    /// <summary>
    /// The material of chainmail armour.
    /// </summary>
    Chain = -1
}
