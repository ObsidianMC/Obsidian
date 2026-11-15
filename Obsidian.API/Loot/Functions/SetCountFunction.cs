using Obsidian.API.Inventory;
using Obsidian.API.Loot.Numbers;

namespace Obsidian.API.Loot.Functions;

/// <summary>
/// Sets the stack size, or adds to it when <see cref="Add"/> is set. A result of 0 or less empties the stack.
/// </summary>
[LootType("minecraft:set_count")]
public sealed class SetCountFunction : LootFunction
{
    /// <summary>
    /// Gets the count to set, or to add when <see cref="Add"/> is set (vanilla's <c>count</c>).
    /// </summary>
    public required INumberProvider Count { get; init; }

    /// <summary>
    /// Gets whether <see cref="Count"/> is added to the stack's current count instead of replacing it (vanilla's
    /// <c>add</c>).
    /// </summary>
    public bool Add { get; init; }

    /// <inheritdoc/>
    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        var start = this.Add && !LootItems.IsEmpty(stack) ? stack.Count : 0;
        stack.Count = start + this.Count.GetInt(context);
        return stack;
    }
}
