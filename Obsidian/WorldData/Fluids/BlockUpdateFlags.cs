namespace Obsidian.WorldData.Fluids;

/// <summary>
/// The subset of vanilla's <c>Block.UPDATE_*</c> flags that block changes from fluids use.
/// </summary>
[Flags]
internal enum BlockUpdateFlags
{
    None = 0,

    /// <summary>
    /// Vanilla <c>UPDATE_NEIGHBORS</c>: the six neighbors get <c>neighborChanged</c>.
    /// </summary>
    Neighbors = 1,

    /// <summary>
    /// Vanilla <c>UPDATE_CLIENTS</c>: players see the change.
    /// </summary>
    Clients = 2,

    /// <summary>
    /// Vanilla <c>UPDATE_KNOWN_SHAPE</c>: neighbors don't get shape updates.
    /// </summary>
    KnownShape = 16,

    /// <summary>
    /// Vanilla <c>UPDATE_SUPPRESS_DROPS</c>: blocks destroyed by the shape updates don't drop items.
    /// </summary>
    SuppressDrops = 32,

    /// <summary>
    /// Vanilla <c>UPDATE_SKIP_ON_PLACE</c>: the new block's <c>onPlace</c> doesn't run.
    /// </summary>
    SkipOnPlace = 512,

    /// <summary>
    /// Vanilla <c>UPDATE_ALL</c>, what <c>setBlockAndUpdate</c> uses.
    /// </summary>
    All = Neighbors | Clients
}
