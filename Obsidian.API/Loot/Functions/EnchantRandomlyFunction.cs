using System.Text.Json;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;

namespace Obsidian.API.Loot.Functions;

/// <summary>
/// Adds one random enchantment at a random level. A book becomes a single enchanted book.
/// </summary>
[LootType("minecraft:enchant_randomly")]
public sealed class EnchantRandomlyFunction : LootFunction
{
    /// <summary>
    /// The enchantments to pick from; every enchantment when null.
    /// </summary>
    public ImmutableArray<EnchantmentDefinition>? Options { get; init; }

    internal JsonElement? OptionsSet { get; init; }

    /// <summary>
    /// Only picks enchantments that support the item. Books accept any enchantment either way.
    /// </summary>
    public bool OnlyCompatible { get; init; } = true;

    /// <summary>
    /// Adds a price for the enchantment to a trade selling the stack, when the context allows it
    /// (<see cref="LootContext.AdditionalCostComponentAllowed"/>).
    /// </summary>
    public bool IncludeAdditionalCostComponent { get; init; }

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        var isBook = LootItems.Is(stack, ItemsRegistry.Book);
        var onlyCompatible = !isBook && this.OnlyCompatible;
        var resolved = LootTableJson.ReadEnchantmentOptions(this.OptionsSet) ?? this.Options;
        IReadOnlyList<EnchantmentDefinition> options = resolved.HasValue ? resolved.Value : EnchantmentsRegistry.All;
        var candidates = options
            .Where(enchantment => !onlyCompatible || enchantment.CanEnchant(stack))
            .ToList();

        // Vanilla logs "Couldn't find a compatible enchantment" and leaves the stack alone.
        if (candidates.Count == 0)
            return stack;

        var chosen = candidates[context.Random.NextInt(candidates.Count)];
        var level = LootItems.NextInt(context.Random, chosen.MinLevel, chosen.MaxLevel);
        if (isBook)
            stack = new ItemStack(ItemsRegistry.EnchantedBook);

        EnchantmentHelper.Enchant(stack, chosen, level);

        // Vanilla's price for an enchanted book: 2 + a draw below 5 + 10 per level, plus 3 per level.
        if (this.IncludeAdditionalCostComponent && context.AdditionalCostComponentAllowed)
        {
            var cost = 2 + context.Random.NextInt(5 + level * 10) + 3 * level;
            LootItems.Set(stack, ComponentBuilder.AdditionalTradeCost with { Value = cost });
        }

        return stack;
    }
}
