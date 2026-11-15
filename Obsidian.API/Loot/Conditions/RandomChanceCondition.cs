using Obsidian.API.Loot.Numbers;

namespace Obsidian.API.Loot.Conditions;

/// <summary>
/// Passes with probability <see cref="Chance"/>.
/// </summary>
[LootType("minecraft:random_chance")]
public sealed class RandomChanceCondition : ILootCondition
{
    /// <summary>
    /// Gets the probability of passing, from 0 to 1 (vanilla's <c>chance</c>); evaluated on each test.
    /// </summary>
    public required INumberProvider Chance { get; init; }

    /// <inheritdoc/>
    public bool Test(LootContext context)
    {
        var chance = this.Chance.GetFloat(context);
        return context.Random.NextFloat() < chance;
    }
}
