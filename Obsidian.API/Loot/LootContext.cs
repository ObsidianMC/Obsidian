using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

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
    public VectorD? Origin { get; init; }

    /// <summary>
    /// The entity the loot is generated for (vanilla's <c>this_entity</c> parameter), e.g. the player opening a container.
    /// </summary>
    public IEntity? ThisEntity { get; init; }

    /// <summary>The stack used to break a block, or null for an empty hand or explosion.</summary>
    public ItemStack? Tool { get; init; }

    /// <summary>The block before removal, for block predicates and state copying.</summary>
    public IBlock? BlockState { get; init; }

    /// <summary>The state's named values, supplied by the level.</summary>
    public IReadOnlyDictionary<string, string> BlockProperties { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>Radius when explosion drop decay is enabled; null makes all items survive.</summary>
    public float? ExplosionRadius { get; init; }

    /// <summary>Components exposed by the broken block entity for copy_components.</summary>
    public IReadOnlyList<DataComponent> BlockEntityComponents { get; init; } = [];

    /// <summary>Dynamic drops supplied by a block entity, such as a decorated pot's contents.</summary>
    public Action<string, Action<ItemStack>>? DynamicDrops { get; init; }

    /// <summary>Tests a location predicate, including its offsets, against the containing level.</summary>
    public Func<System.Text.Json.JsonElement, bool>? TestLocation { get; init; }

    /// <summary>
    /// Resolves the tables referenced by <c>minecraft:loot_table</c> entries. Defaults to the vanilla tables in
    /// <see cref="LootTables.All"/>; unknown ids generate nothing.
    /// </summary>
    public Func<string, LootTable?> ResolveTable { get; init; } = id => LootTables.All.GetValueOrDefault(id);

    /// <summary>
    /// Creates the explorer maps of <c>minecraft:exploration_map</c> functions, or <c>null</c> where the level can't (the map
    /// is then left as is).
    /// </summary>
    public IExplorationMapFactory? ExplorationMaps { get; init; }

    /// <summary>
    /// Marks <paramref name="table"/> as being generated; returns false when it already is, which means tables
    /// reference each other in a loop.
    /// </summary>
    internal bool TryEnter(LootTable table) => this.visitedTables.Add(table);

    internal void Exit(LootTable table) => this.visitedTables.Remove(table);
}
