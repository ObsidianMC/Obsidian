using Obsidian.API.Inventory;
using Obsidian.API.Loot.Conditions;
using Obsidian.API.Loot.Functions;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.Loot.Entries;

/// <summary>
/// An entry that can be picked by a pool roll, like vanilla's <c>LootPoolSingletonContainer</c>.
/// </summary>
public abstract class LootPoolSingletonEntry : LootPoolEntry
{
    public int Weight { get; init; } = 1;

    /// <summary>
    /// Added to <see cref="Weight"/> once per point of luck.
    /// </summary>
    public int Quality { get; init; }

    /// <summary>
    /// Applied to every stack this entry generates.
    /// </summary>
    public LootFunction[] Functions { get; init; } = [];

    public override bool Expand(LootContext context, Action<LootPoolSingletonEntry> output)
    {
        if (!ILootCondition.TestAll(this.Conditions, context))
            return false;

        output(this);
        return true;
    }

    public int GetWeight(float luck) => Math.Max(Mth.Floor(this.Weight + this.Quality * luck), 0);

    /// <summary>
    /// Generates this entry's stacks, passes each through <see cref="Functions"/> and then to <paramref name="output"/>.
    /// </summary>
    public void CreateItemStacks(Action<ItemStack> output, LootContext context) =>
        this.CreateItems(LootFunction.Decorate(this.Functions, output, context), context);

    protected abstract void CreateItems(Action<ItemStack> output, LootContext context);
}
