namespace Obsidian.API.Inventory;

/// <summary>
/// An enchantment on an item and its level.
/// </summary>
public readonly record struct Enchantment
{
    /// <summary>
    /// Gets the enchantment's index in vanilla's enchantment registry, which is sorted by id (see
    /// <see cref="Registries.EnchantmentsRegistry"/>).
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Gets the enchantment's level, from 1.
    /// </summary>
    public required int Level { get; init; }
}
