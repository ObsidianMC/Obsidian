using Obsidian.API.Inventory;

namespace Obsidian.API.Loot.Entries;

/// <summary>
/// Generates the items of another loot table, resolved by id through <see cref="LootContext.ResolveTable"/>.
/// </summary>
[LootType("minecraft:loot_table")]
public sealed class LootTableEntry : LootPoolSingletonEntry
{
    /// <summary>
    /// The referenced table's id, e.g. <c>minecraft:chests/trial_chambers/reward_rare</c>.
    /// </summary>
    public required string Value { get; init; }

    protected override void CreateItems(Action<ItemStack> output, LootContext context) =>
        (context.ResolveTable(this.Value) ?? LootTable.Empty).GetRandomItemsRaw(context, output);
}
