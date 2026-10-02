using Obsidian.API.Inventory;
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
    public EnchantmentDefinition[]? Options { get; init; }

    /// <summary>
    /// Only picks enchantments that support the item. Books accept any enchantment either way.
    /// </summary>
    public bool OnlyCompatible { get; init; } = true;

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        var isBook = LootItems.Is(stack, Material.Book);
        var onlyCompatible = !isBook && this.OnlyCompatible;
        var candidates = (this.Options ?? EnchantmentsRegistry.All)
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
        return stack;
    }
}
