using Obsidian.API.Inventory;
using Obsidian.API.Loot.Conditions;
using Obsidian.API.Loot.Entries;
using Obsidian.API.Loot.Functions;
using Obsidian.API.Loot.Numbers;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.Loot;

/// <summary>
/// A set of weighted entries rolled a number of times, like vanilla's <c>LootPool</c>.
/// </summary>
public sealed class LootPool
{
    /// <summary>
    /// Gets the entries a roll picks from, by weight (vanilla's <c>entries</c>).
    /// </summary>
    public required ImmutableArray<LootPoolEntry> Entries { get; init; }

    /// <summary>
    /// The pool is skipped unless all of these pass.
    /// </summary>
    public ImmutableArray<ILootCondition> Conditions { get; init; } = [];

    /// <summary>
    /// Applied to every stack the pool generates, after the entry's own functions.
    /// </summary>
    public ImmutableArray<LootFunction> Functions { get; init; } = [];

    /// <summary>
    /// Gets how many times the pool is rolled, before bonus rolls for luck (vanilla's <c>rolls</c>).
    /// </summary>
    public required INumberProvider Rolls { get; init; }

    /// <summary>
    /// Extra rolls per point of luck.
    /// </summary>
    public INumberProvider BonusRolls { get; init; } = new ConstantNumber { Value = 0 };

    /// <summary>
    /// Rolls the pool if its <see cref="Conditions"/> pass, passing each generated stack, after the pool's
    /// <see cref="Functions"/>, to <paramref name="output"/>.
    /// </summary>
    public void AddRandomItems(Action<ItemStack> output, LootContext context)
    {
        if (!ILootCondition.TestAll(this.Conditions, context))
            return;

        var decorated = LootFunction.Decorate(this.Functions, output, context);
        var rolls = this.Rolls.GetInt(context) + Mth.Floor(this.BonusRolls.GetFloat(context) * context.Luck);

        for (var i = 0; i < rolls; i++)
            this.AddRandomItem(decorated, context);
    }

    private void AddRandomItem(Action<ItemStack> output, LootContext context)
    {
        var candidates = new List<LootPoolSingletonEntry>();
        var totalWeight = 0;

        foreach (var entry in this.Entries)
        {
            entry.Expand(context, candidate =>
            {
                var weight = candidate.GetWeight(context.Luck);
                if (weight > 0)
                {
                    candidates.Add(candidate);
                    totalWeight += weight;
                }
            });
        }

        if (totalWeight == 0 || candidates.Count == 0)
            return;

        // A single candidate is taken without drawing a number.
        if (candidates.Count == 1)
        {
            candidates[0].CreateItemStacks(output, context);
            return;
        }

        var pick = context.Random.NextInt(totalWeight);
        foreach (var candidate in candidates)
        {
            pick -= candidate.GetWeight(context.Luck);
            if (pick < 0)
            {
                candidate.CreateItemStacks(output, context);
                return;
            }
        }
    }
}
