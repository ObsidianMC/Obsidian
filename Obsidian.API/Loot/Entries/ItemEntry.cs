using Obsidian.API.Inventory;

namespace Obsidian.API.Loot.Entries;

/// <summary>
/// Generates one item of <see cref="Name"/>.
/// </summary>
[LootType("minecraft:item")]
public sealed class ItemEntry : LootPoolSingletonEntry
{
    /// <summary>
    /// Gets the item the entry generates, as a stack of one before <see cref="LootPoolSingletonEntry.Functions"/> run
    /// (vanilla's <c>name</c>).
    /// </summary>
    public required Item Name { get; init; }

    /// <inheritdoc/>
    protected override void CreateItems(Action<ItemStack> output, LootContext context) => output(new ItemStack(this.Name));
}
