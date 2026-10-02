using Obsidian.API.Inventory;
using Obsidian.API.Loot.Numbers;
using Obsidian.API.Registries;

namespace Obsidian.API.Loot.Functions;

/// <summary>
/// Enchants the stack like an enchanting table at <see cref="Levels"/> levels. A book becomes a single enchanted book.
/// </summary>
[LootType("minecraft:enchant_with_levels")]
public sealed class EnchantWithLevelsFunction : LootFunction
{
    public required INumberProvider Levels { get; init; }

    /// <summary>
    /// The enchantments to pick from; every enchantment when null.
    /// </summary>
    public EnchantmentDefinition[]? Options { get; init; }

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        var levels = this.Levels.GetInt(context);
        return EnchantmentHelper.EnchantItem(context.Random, stack, levels, this.Options ?? EnchantmentsRegistry.All);
    }
}
