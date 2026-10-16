using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
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
    public VectorD? Origin { get; init; }

    /// <summary>
    /// The entity the loot is generated for (vanilla's <c>this_entity</c> parameter), e.g. the player opening a container.
    /// </summary>
    public IEntity? ThisEntity { get; init; }

    /// <summary>
    /// The block state being broken or exploded (vanilla <c>BLOCK_STATE</c>), read before the block is removed.
    /// </summary>
    public IBlock? Block { get; init; }

    /// <summary>
    /// The tool the block was broken with (vanilla <c>TOOL</c>): an empty stack for bare hands. Null when no tool was given,
    /// which makes tool-dependent functions leave the stack unchanged, as vanilla does.
    /// </summary>
    public ItemStack? Tool { get; init; }

    /// <summary>
    /// The explosion radius (vanilla <c>EXPLOSION_RADIUS</c>); null for ordinary breaks.
    /// </summary>
    public float? ExplosionRadius { get; init; }

    /// <summary>
    /// Reads a vanilla block state property (e.g. <c>half</c>) of a block; null when the block doesn't have it. Required
    /// by block-state conditions and <c>copy_state</c>.
    /// </summary>
    public Func<IBlock, string, string?>? BlockProperty { get; init; }

    /// <summary>
    /// Returns the block at a block position, or null when it isn't loaded. Required by <c>location_check</c>.
    /// </summary>
    public Func<int, int, int, IBlock?>? BlockAt { get; init; }

    /// <summary>
    /// The components the block's block entity contributes (vanilla's <c>copy_components</c> with source
    /// <c>block_entity</c>), read before the block is removed. Null when the block had no block entity, which copies
    /// nothing. Returns null for a component the block entity doesn't hold; throws <see cref="NotSupportedException"/>
    /// for one it holds but that isn't modelled yet.
    /// </summary>
    public Func<string, DataComponent?>? BlockEntityComponent { get; init; }

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
