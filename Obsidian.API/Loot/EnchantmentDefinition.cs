using Obsidian.API.Inventory;

namespace Obsidian.API.Loot;

/// <summary>
/// The part of a vanilla enchantment definition that decides where it can be applied and at which level, used to
/// enchant generated loot. The vanilla enchantments are in <see cref="Registries.EnchantmentsRegistry"/>.
/// </summary>
public sealed class EnchantmentDefinition
{
    /// <summary>
    /// Registry id, e.g. <c>minecraft:sharpness</c>.
    /// </summary>
    public required string Identifier { get; init; }

    /// <summary>
    /// Index in vanilla's enchantment registry, which is sorted by id. This is the id <see cref="Enchantment.Id"/> holds.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Items the enchantment can be applied to.
    /// </summary>
    public required ItemSet SupportedItems { get; init; }

    /// <summary>
    /// Items enchanting (tables and loot enchanted with levels) offers the enchantment for; all supported items when null.
    /// </summary>
    public ItemSet? PrimaryItems { get; init; }

    /// <summary>
    /// Relative chance of being picked by <see cref="EnchantmentHelper.SelectEnchantments"/>.
    /// </summary>
    public required int Weight { get; init; }

    public required int MaxLevel { get; init; }

    public int MinLevel => 1;

    /// <summary>
    /// The lowest enchanting power that gives each level.
    /// </summary>
    public required EnchantmentCost MinCost { get; init; }

    /// <summary>
    /// The highest enchanting power that gives each level.
    /// </summary>
    public required EnchantmentCost MaxCost { get; init; }

    /// <summary>
    /// Ids of the enchantments this one can't be combined with.
    /// </summary>
    public ImmutableArray<int> ExclusiveSet { get; init; } = [];

    /// <summary>
    /// Whether the enchantment can be applied to the stack's item.
    /// </summary>
    public bool CanEnchant(ItemStack stack) => this.SupportedItems.Contains(LootItems.ItemOf(stack));

    public bool IsPrimaryItem(ItemStack stack) =>
        this.CanEnchant(stack) && (this.PrimaryItems is null || this.PrimaryItems.Contains(LootItems.ItemOf(stack)));

    /// <summary>
    /// Whether two different enchantments may be on the same item.
    /// </summary>
    public static bool AreCompatible(EnchantmentDefinition first, EnchantmentDefinition second) =>
        first.Id != second.Id && !first.ExclusiveSet.Contains(second.Id) && !second.ExclusiveSet.Contains(first.Id);
}

/// <summary>
/// An enchanting power that grows linearly with the enchantment level.
/// </summary>
public readonly record struct EnchantmentCost(int Base, int PerLevelAboveFirst)
{
    public int Calculate(int level) => this.Base + this.PerLevelAboveFirst * (level - 1);
}
