using Obsidian.API.Inventory;

namespace Obsidian.API.Loot.Entries;

/// <summary>
/// Generates nothing; its weight makes the other entries of the pool less likely.
/// </summary>
[LootType("minecraft:empty")]
public sealed class EmptyEntry : LootPoolSingletonEntry
{
    protected override void CreateItems(Action<ItemStack> output, LootContext context) { }
}
