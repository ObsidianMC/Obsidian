namespace Obsidian.API.Blocks;

/// <summary>
/// What else happens when a block changes, like vanilla's <c>Block.UPDATE_*</c> flags (with vanilla's values).
/// </summary>
[Flags]
public enum BlockUpdateFlags
{
    /// <summary>
    /// Only the block changes: players aren't told, and neighbours still get shape updates unless
    /// <see cref="KnownShape"/> is set too.
    /// </summary>
    None = 0,

    /// <summary>
    /// Vanilla <c>UPDATE_NEIGHBORS</c>: the six neighbours hear their neighbour changed, and the block it replaced is
    /// told it was removed.
    /// </summary>
    Neighbors = 1,

    /// <summary>
    /// Vanilla <c>UPDATE_CLIENTS</c>: players see the change.
    /// </summary>
    Clients = 2,

    /// <summary>
    /// Vanilla <c>UPDATE_KNOWN_SHAPE</c>: neighbours don't get shape updates.
    /// </summary>
    KnownShape = 16,

    /// <summary>
    /// Vanilla <c>UPDATE_SUPPRESS_DROPS</c>: blocks the shape updates destroy don't drop anything.
    /// </summary>
    SuppressDrops = 32,

    /// <summary>
    /// Vanilla <c>UPDATE_SKIP_ON_PLACE</c>: the new block isn't told it was placed.
    /// </summary>
    SkipOnPlace = 512,

    /// <summary>
    /// Vanilla <c>UPDATE_ALL</c>, what a player placing or breaking a block uses.
    /// </summary>
    All = Neighbors | Clients
}
