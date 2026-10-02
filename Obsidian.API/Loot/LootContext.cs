using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.Loot;

/// <summary>
/// The state shared while generating loot from a table, like vanilla's <c>LootContext</c>: the random source every
/// draw comes from, the luck and the optional context parameters.
/// </summary>
/// <remarks>
/// A context is used for a single generation; it isn't thread-safe. Use <see cref="LootTable.CreateRandom"/> to seed
/// <see cref="Random"/> the way vanilla does for a stored loot table seed.
/// </remarks>
public sealed class LootContext
{
    private readonly HashSet<LootTable> visitedTables = [];

    public required IRandomSource Random { get; init; }

    /// <summary>
    /// Raises the weight of entries with a quality and adds bonus rolls. For containers this is the opening player's
    /// luck attribute, 0 without luck effects.
    /// </summary>
    public float Luck { get; init; }

    /// <summary>
    /// Where the loot is generated (vanilla's <c>origin</c> parameter); for containers, the center of the block.
    /// </summary>
    public VectorF? Origin { get; init; }

    /// <summary>
    /// The entity the loot is generated for (vanilla's <c>this_entity</c> parameter), e.g. the player opening a container.
    /// </summary>
    public IEntity? ThisEntity { get; init; }

    /// <summary>
    /// Resolves the tables referenced by <c>minecraft:loot_table</c> entries. Defaults to the vanilla tables in
    /// <see cref="LootTables.All"/>; unknown ids generate nothing.
    /// </summary>
    public Func<string, LootTable?> ResolveTable { get; init; } = id => LootTables.All.GetValueOrDefault(id);

    /// <summary>
    /// Marks <paramref name="table"/> as being generated; returns false when it already is, which means tables
    /// reference each other in a loop.
    /// </summary>
    internal bool TryEnter(LootTable table) => this.visitedTables.Add(table);

    internal void Exit(LootTable table) => this.visitedTables.Remove(table);
}
