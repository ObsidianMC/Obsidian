using Obsidian.API.Loot.Numbers;

namespace Obsidian.API.Loot.Conditions;

/// <summary>
/// Passes with probability <see cref="Chance"/>.
/// </summary>
[LootType("minecraft:random_chance")]
public sealed class RandomChanceCondition : ILootCondition
{
    public required INumberProvider Chance { get; init; }

    public bool Test(LootContext context)
    {
        var chance = this.Chance.GetFloat(context);
        return context.Random.NextFloat() < chance;
    }
}
