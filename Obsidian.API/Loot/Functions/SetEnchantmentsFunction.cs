using Obsidian.API.Inventory;
using Obsidian.API.Loot.Numbers;
using Obsidian.API.Registries;

namespace Obsidian.API.Loot.Functions;

/// <summary>
/// Sets the levels of the given enchantments (a level of 0 removes it), or adds to them when <see cref="Add"/> is set.
/// A book becomes an enchanted book with the same count.
/// </summary>
[LootType("minecraft:set_enchantments")]
public sealed class SetEnchantmentsFunction : LootFunction
{
    /// <summary>
    /// The enchantments and their levels, evaluated in order.
    /// </summary>
    public Dictionary<EnchantmentDefinition, INumberProvider> Enchantments { get; init; } = [];

    public bool Add { get; init; }

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        if (LootItems.Is(stack, Material.Book))
            stack = stack.TransmuteCopy(ItemsRegistry.EnchantedBook);

        var enchantments = EnchantmentHelper.GetEnchantments(stack).ToList();
        foreach (var (enchantment, levelProvider) in this.Enchantments)
        {
            var index = enchantments.FindIndex(existing => existing.Id == enchantment.Id);
            var current = index < 0 ? 0 : enchantments[index].Level;
            var level = Math.Clamp(this.Add ? current + levelProvider.GetInt(context) : levelProvider.GetInt(context), 0, 255);

            var updated = new Enchantment { Id = enchantment.Id, Level = level };
            if (index < 0 && level > 0)
                enchantments.Add(updated);
            else if (index >= 0 && level > 0)
                enchantments[index] = updated;
            else if (index >= 0)
                enchantments.RemoveAt(index);
        }

        EnchantmentHelper.SetEnchantments(stack, [.. enchantments]);
        return stack;
    }
}
