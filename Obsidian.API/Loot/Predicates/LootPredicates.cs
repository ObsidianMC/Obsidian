using Obsidian.API.Inventory;
using Obsidian.API.Loot.Numbers;

namespace Obsidian.API.Loot.Predicates;

/// <summary>
/// The item part of a <c>minecraft:match_tool</c> predicate: an item set and/or enchantment requirements on the stack.
/// </summary>
public sealed class ToolPredicate
{
    public ItemSet? Items { get; init; }

    /// <summary>
    /// Keyed by component id; only <c>minecraft:enchantments</c> appears in vanilla block tables.
    /// </summary>
    public Dictionary<string, ImmutableArray<EnchantmentPredicate>>? Predicates { get; init; }

    /// <summary>
    /// Whether the stack matches: its item is in <see cref="Items"/> and every enchantment predicate matches.
    /// </summary>
    public bool Matches(ItemStack tool, LootContext context)
    {
        if (this.Items is not null && !this.Items.Contains(tool.Holder))
            return false;

        if (this.Predicates is null)
            return true;

        foreach (var (component, predicates) in this.Predicates)
        {
            if (component != "minecraft:enchantments")
                throw new NotSupportedException($"match_tool can't test the {component} component yet.");

            var enchantments = EnchantmentHelper.GetEnchantments(tool);
            foreach (var predicate in predicates)
            {
                if (!predicate.Matches(enchantments, context))
                    return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Requires the stack to carry <see cref="Enchantments"/> at a level within <see cref="Levels"/>.
/// </summary>
public sealed class EnchantmentPredicate
{
    /// <summary>
    /// The enchantment; any enchantment when null.
    /// </summary>
    public EnchantmentDefinition? Enchantments { get; init; }

    public NumberRange? Levels { get; init; }

    /// <summary>
    /// Whether one of the enchantments is <see cref="Enchantments"/> (or any) at a level within <see cref="Levels"/>.
    /// </summary>
    public bool Matches(Enchantment[] enchantments, LootContext context)
    {
        foreach (var enchantment in enchantments)
        {
            if ((this.Enchantments is null || enchantment.Id == this.Enchantments.Id)
                && (this.Levels is null || this.Levels.Contains(enchantment.Level, context)))
                return true;
        }

        return false;
    }
}

/// <summary>
/// A range with optional bounds, like vanilla's <c>MinMaxBounds</c>.
/// </summary>
public sealed class NumberRange
{
    public INumberProvider? Min { get; init; }

    public INumberProvider? Max { get; init; }

    /// <summary>
    /// Whether <paramref name="value"/> is within the bounds that are set (min before max, like vanilla).
    /// </summary>
    public bool Contains(int value, LootContext context) =>
        (this.Min is null || value >= this.Min.GetInt(context)) && (this.Max is null || value <= this.Max.GetInt(context));
}

/// <summary>
/// The block part of a location predicate: block ids and block state properties by vanilla name.
/// </summary>
public sealed class BlockPredicate
{
    public string? Blocks { get; init; }

    public Dictionary<string, string>? State { get; init; }

    /// <summary>
    /// Whether the block is <see cref="Blocks"/> (any block when null) with every <see cref="State"/> value.
    /// </summary>
    public bool Matches(IBlock block, LootContext context)
    {
        if (this.Blocks is not null && block.UnlocalizedName != this.Blocks)
            return false;

        if (this.State is null)
            return true;

        var property = context.BlockProperty ?? throw new InvalidOperationException("The loot context has no block property reader.");
        foreach (var (name, value) in this.State)
        {
            if (property(block, name) != value)
                return false;
        }

        return true;
    }
}

/// <summary>
/// Tests the block at an offset from the loot origin, like vanilla's <c>LocationPredicate</c>.
/// </summary>
public sealed class LocationPredicate
{
    public BlockPredicate? Block { get; init; }
}

/// <summary>
/// Tests an entity. Block tables only use the empty predicate on <c>this</c>.
/// </summary>
public sealed class EntityPredicate
{
}
