using Obsidian.API.Loot.Conditions;

namespace Obsidian.API.Loot.Entries;

/// <summary>
/// An entry of a loot pool, like vanilla's <c>LootPoolEntryContainer</c>.
/// </summary>
public abstract class LootPoolEntry
{
    /// <summary>
    /// The entry only takes part in a roll when all of these pass.
    /// </summary>
    public ILootCondition[] Conditions { get; init; } = [];

    /// <summary>
    /// Passes the entries this one contributes to a roll to <paramref name="output"/>; returns whether it contributed.
    /// </summary>
    public abstract bool Expand(LootContext context, Action<LootPoolSingletonEntry> output);
}
