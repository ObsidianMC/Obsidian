using Obsidian.API.Inventory;
using Obsidian.API.Loot.Functions;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.Loot;

/// <summary>
/// A vanilla loot table: pools of weighted entries that generate item stacks. The vanilla tables are in
/// <see cref="Registries.LootTables"/>.
/// </summary>
/// <remarks>
/// Generating with a context seeded from <see cref="CreateRandom"/> gives the same items in the same order (and
/// <see cref="Fill"/> the same slots) as vanilla for the same seed.
/// </remarks>
public sealed class LootTable
{
    /// <summary>
    /// A table that generates nothing; vanilla uses it for unknown ids.
    /// </summary>
    public static LootTable Empty { get; } = new();

    /// <summary>
    /// Registry id (e.g. <c>minecraft:chests/simple_dungeon</c>), or empty for tables defined inline.
    /// </summary>
    public string Identifier { get; init; } = string.Empty;

    /// <summary>
    /// The context parameter set the table is meant for, e.g. <c>minecraft:chest</c> or <c>minecraft:archaeology</c>.
    /// </summary>
    public string Type { get; init; } = "minecraft:generic";

    /// <summary>
    /// The level random sequence used when the table is generated without a seed (see <see cref="CreateRandom"/>).
    /// </summary>
    public string? RandomSequence { get; init; }

    public LootPool[] Pools { get; init; } = [];

    /// <summary>
    /// Applied to every stack the table generates, after the pool's functions.
    /// </summary>
    public LootFunction[] Functions { get; init; } = [];

    /// <summary>
    /// Creates the random source vanilla uses for a stored loot table seed: a non-zero seed seeds a fresh
    /// <see cref="LegacyRandomSource"/>; seed 0 means "unseeded" and takes the next values of the level's random
    /// sequence for <see cref="RandomSequence"/>, or an arbitrary seed when there is none.
    /// </summary>
    public IRandomSource CreateRandom(long seed, RandomSequences? sequences = null)
    {
        if (seed != 0L)
            return new LegacyRandomSource(seed);

        if (this.RandomSequence is not null && sequences is not null)
            return sequences.Get(this.RandomSequence);

        return new LegacyRandomSource(Random.Shared.NextInt64());
    }

    /// <summary>
    /// Generates the table's stacks without splitting oversized stacks. Used for nested tables.
    /// </summary>
    public void GetRandomItemsRaw(LootContext context, Action<ItemStack> output)
    {
        // Vanilla logs "Detected infinite loop in loot tables" and generates nothing.
        if (!context.TryEnter(this))
            return;

        var decorated = LootFunction.Decorate(this.Functions, output, context);
        foreach (var pool in this.Pools)
            pool.AddRandomItems(decorated, context);

        context.Exit(this);
    }

    /// <summary>
    /// Generates the table's stacks. Stacks above their item's max stack size are split into full stacks and a
    /// remainder, like vanilla's stack splitter. Stacks that functions emptied are kept, as in vanilla.
    /// </summary>
    public List<ItemStack> GetRandomItems(LootContext context)
    {
        var items = new List<ItemStack>();
        this.GetRandomItemsRaw(context, stack =>
        {
            var maxStackSize = stack.Holder.MaxStackSize;
            if (stack.Count < maxStackSize)
            {
                items.Add(stack);
                return;
            }

            for (var remaining = stack.Count; remaining > 0; remaining -= maxStackSize)
                items.Add(new ItemStack(stack, Math.Min(maxStackSize, remaining)));
        });

        return items;
    }

    /// <summary>
    /// Generates the table's stacks into the empty slots of <paramref name="container"/>, like vanilla's
    /// <c>LootTable.fill</c>: stacks are split up to spread over the free slots, shuffled and put in random slots.
    /// </summary>
    public void Fill(BaseContainer container, LootContext context)
    {
        var items = this.GetRandomItems(context);
        var random = context.Random;
        var slots = GetAvailableSlots(container, random);
        items = ShuffleAndSplitItems(items, slots.Count, random);

        foreach (var stack in items)
        {
            // Vanilla logs "Tried to over-fill a container" and drops the rest.
            if (slots.Count == 0)
                return;

            container.SetItem(slots[^1], stack);
            slots.RemoveAt(slots.Count - 1);
        }
    }

    private static List<int> GetAvailableSlots(BaseContainer container, IRandomSource random)
    {
        var slots = new List<int>();
        for (var i = 0; i < container.Size; i++)
        {
            if (LootItems.IsEmpty(container.GetItem(i)))
                slots.Add(i);
        }

        LootItems.Shuffle(slots, random);
        return slots;
    }

    /// <summary>
    /// Drops empty stacks, then splits random stacks in two until the stacks would fill <paramref name="slotCount"/>
    /// slots (or none can be split further), and shuffles the result.
    /// </summary>
    private static List<ItemStack> ShuffleAndSplitItems(List<ItemStack> items, int slotCount, IRandomSource random)
    {
        var result = new List<ItemStack>();
        var splittable = new List<ItemStack>();
        foreach (var stack in items)
        {
            if (LootItems.IsEmpty(stack))
                continue;

            if (stack.Count > 1)
                splittable.Add(stack);
            else
                result.Add(stack);
        }

        while (slotCount - result.Count - splittable.Count > 0 && splittable.Count > 0)
        {
            var index = LootItems.NextInt(random, 0, splittable.Count - 1);
            var stack = splittable[index];
            splittable.RemoveAt(index);

            var split = LootItems.Split(stack, LootItems.NextInt(random, 1, stack.Count / 2));
            if (stack.Count > 1 && random.NextBoolean())
                splittable.Add(stack);
            else
                result.Add(stack);

            if (split.Count > 1 && random.NextBoolean())
                splittable.Add(split);
            else
                result.Add(split);
        }

        result.AddRange(splittable);
        LootItems.Shuffle(result, random);
        return result;
    }
}
