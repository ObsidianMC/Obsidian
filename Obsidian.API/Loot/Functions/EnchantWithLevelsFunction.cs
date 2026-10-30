using System.Text.Json;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
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
    public ImmutableArray<EnchantmentDefinition>? Options { get; init; }

    internal JsonElement? OptionsSet { get; init; }

    /// <summary>
    /// Adds the levels to the price of a trade selling the stack, when the context allows it
    /// (<see cref="LootContext.AdditionalCostComponentAllowed"/>).
    /// </summary>
    public bool IncludeAdditionalCostComponent { get; init; }

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        var levels = this.Levels.GetInt(context);
        var resolved = LootTableJson.ReadEnchantmentOptions(this.OptionsSet) ?? this.Options;
        IReadOnlyList<EnchantmentDefinition> options = resolved.HasValue ? resolved.Value : EnchantmentsRegistry.All;
        var enchanted = EnchantmentHelper.EnchantItem(context.Random, stack, levels, options);

        var addsCost = this.IncludeAdditionalCostComponent && context.AdditionalCostComponentAllowed;
        if (addsCost && !LootItems.IsEmpty(enchanted) && levels > 0)
            LootItems.Set(enchanted, ComponentBuilder.AdditionalTradeCost with { Value = levels });

        return enchanted;
    }
}
