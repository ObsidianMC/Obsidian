using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using System.Diagnostics;

namespace Obsidian.API.Loot;

/// <summary>
/// Vanilla's enchanting logic (<c>EnchantmentHelper</c>) for item stacks: picking enchantments for an enchanting power
/// and adding them to a stack.
/// </summary>
public static class EnchantmentHelper
{
    /// <summary>
    /// Enchants <paramref name="stack"/> like an enchanting table would at <paramref name="level"/> levels, picking from
    /// <paramref name="options"/>. A book becomes a new single enchanted book; other stacks are enchanted in place.
    /// </summary>
    public static ItemStack EnchantItem(IRandomSource random, ItemStack stack, int level, IReadOnlyList<EnchantmentDefinition> options)
    {
        var selected = SelectEnchantments(random, stack, level, options);
        if (LootItems.Is(stack, Material.Book))
            stack = new ItemStack(ItemsRegistry.EnchantedBook);

        foreach (var (enchantment, enchantmentLevel) in selected)
            Enchant(stack, enchantment, enchantmentLevel);

        return stack;
    }

    /// <summary>
    /// Picks the enchantments (and levels) an enchanting table would give <paramref name="stack"/> at
    /// <paramref name="level"/> levels. Draws nothing for items that can't be enchanted.
    /// </summary>
    public static List<(EnchantmentDefinition Enchantment, int Level)> SelectEnchantments(IRandomSource random, ItemStack stack, int level,
        IReadOnlyList<EnchantmentDefinition> options)
    {
        var selected = new List<(EnchantmentDefinition Enchantment, int Level)>();
        var enchantable = stack.RemoveComponents.Contains(DataComponentType.Enchantable) ? 0 :
            stack.GetComponent<SimpleDataComponent<int>>(DataComponentType.Enchantable)?.Value ?? stack.Holder.Enchantable;
        if (enchantable == 0)
            return selected;

        level += 1 + random.NextInt(enchantable / 4 + 1) + random.NextInt(enchantable / 4 + 1);
        var spread = (random.NextFloat() + random.NextFloat() - 1.0f) * 0.15f;
        level = Math.Max((int)Math.Floor(level + level * spread + 0.5d), 1);

        var available = GetAvailableEnchantments(level, stack, options);
        if (available.Count == 0)
            return selected;

        selected.Add(PickWeighted(random, available));
        while (random.NextInt(50) <= level)
        {
            var last = selected[^1].Enchantment;
            available.RemoveAll(candidate => !EnchantmentDefinition.AreCompatible(last, candidate.Enchantment));

            if (available.Count == 0)
                break;

            selected.Add(PickWeighted(random, available));
            level /= 2;
        }

        return selected;
    }

    /// <summary>
    /// Adds <paramref name="enchantment"/> at <paramref name="level"/> to the stack's enchantments (stored enchantments for
    /// an enchanted book), keeping the higher level if the stack already has it.
    /// </summary>
    public static void Enchant(ItemStack stack, EnchantmentDefinition enchantment, int level)
    {
        var enchantments = GetEnchantments(stack);
        var index = Array.FindIndex(enchantments, existing => existing.Id == enchantment.Id);
        if (index < 0)
            enchantments = [.. enchantments, new Enchantment { Id = enchantment.Id, Level = Math.Min(level, 255) }];
        else if (level > enchantments[index].Level)
            enchantments[index] = enchantments[index] with { Level = Math.Min(level, 255) };

        SetEnchantments(stack, enchantments);
    }

    /// <summary>
    /// The stack's enchantments, or the stored enchantments of an enchanted book. The array is a copy.
    /// </summary>
    public static Enchantment[] GetEnchantments(ItemStack stack)
    {
        var enchantments = LootItems.Is(stack, Material.EnchantedBook)
            ? stack.GetComponent<SimpleDataComponent<Enchantment[]>>(DataComponentType.StoredEnchantments)?.Value
            : stack.GetComponent<SimpleDataComponent<Enchantment[]>>(DataComponentType.Enchantments)?.Value;

        return enchantments is null ? [] : [.. enchantments];
    }

    /// <summary>
    /// Replaces the stack's enchantments, or the stored enchantments of an enchanted book.
    /// </summary>
    public static void SetEnchantments(ItemStack stack, Enchantment[] enchantments)
    {
        if (LootItems.Is(stack, Material.EnchantedBook))
            LootItems.Set(stack, ComponentBuilder.StoredEnchantments with { Value = enchantments });
        else
            LootItems.Set(stack, ComponentBuilder.Enchantments with { Value = enchantments });
    }

    /// <summary>
    /// For each option the stack is a primary item of (any option for a book), the highest level whose cost range
    /// contains <paramref name="level"/>.
    /// </summary>
    private static List<(EnchantmentDefinition Enchantment, int Level)> GetAvailableEnchantments(int level, ItemStack stack,
        IReadOnlyList<EnchantmentDefinition> options)
    {
        var available = new List<(EnchantmentDefinition Enchantment, int Level)>();
        var isBook = LootItems.Is(stack, Material.Book);

        foreach (var enchantment in options)
        {
            if (!isBook && !enchantment.IsPrimaryItem(stack))
                continue;

            for (var enchantmentLevel = enchantment.MaxLevel; enchantmentLevel >= enchantment.MinLevel; enchantmentLevel--)
            {
                if (level >= enchantment.MinCost.Calculate(enchantmentLevel) && level <= enchantment.MaxCost.Calculate(enchantmentLevel))
                {
                    available.Add((enchantment, enchantmentLevel));
                    break;
                }
            }
        }

        return available;
    }

    private static (EnchantmentDefinition Enchantment, int Level) PickWeighted(IRandomSource random,
        List<(EnchantmentDefinition Enchantment, int Level)> candidates)
    {
        var pick = random.NextInt(candidates.Sum(candidate => candidate.Enchantment.Weight));
        foreach (var candidate in candidates)
        {
            pick -= candidate.Enchantment.Weight;
            if (pick < 0)
                return candidate;
        }

        throw new UnreachableException();
    }
}
