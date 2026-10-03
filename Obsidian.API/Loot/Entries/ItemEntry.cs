using Obsidian.API.Inventory;

namespace Obsidian.API.Loot.Entries;

/// <summary>
/// Generates one item of <see cref="Name"/>.
/// </summary>
[LootType("minecraft:item")]
public sealed class ItemEntry : LootPoolSingletonEntry
{
    public required Item Name { get; init; }

    protected override void CreateItems(Action<ItemStack> output, LootContext context) => output(new ItemStack(this.Name));
}
