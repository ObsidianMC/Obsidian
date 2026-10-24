namespace Obsidian.WorldData.BlockNotifications;

/// <summary>
/// The narrow view of a level that block notifications need. Reads never load or generate chunks.
/// </summary>
internal interface IBlockChangeWorld
{
    int MinY { get; }

    int Height { get; }

    /// <summary>
    /// The committed block at <paramref name="position"/>; void air outside the build height, and <c>null</c> when its chunk isn't loaded.
    /// </summary>
    IBlock? GetLoadedBlock(Vector position);

    /// <summary>The raw light at a position, used by vanilla MushroomBlock survival checks.</summary>
    int GetRawBrightness(Vector position);

    /// <summary>
    /// Commits a block and queues its notifications. Used for the state changes shape updates produce.
    /// </summary>
    ValueTask SetBlockAsync(Vector position, IBlock block);

    /// <summary>
    /// Removes an unsupported block through its loot path. Shape updates use this rather than committing air so the
    /// block drops resources before it is gone.
    /// </summary>
    void DropAndRemove(Vector position, IBlock block, bool playEffect);

    /// <summary>
    /// Schedules the block's own fluid tick, like vanilla's <c>updateShape</c> scheduling a tick.
    /// </summary>
    void ScheduleFluidTick(Vector position);

    /// <summary>
    /// Notifies the six neighbors of <paramref name="position"/> and runs the queued work, without changing any block. Vanilla
    /// <c>updateNeighborsAt</c>: a mounted source uses it on its support, so the blocks next to the support react to its power.
    /// </summary>
    ValueTask NotifyNeighborsAsync(Vector position);
}
