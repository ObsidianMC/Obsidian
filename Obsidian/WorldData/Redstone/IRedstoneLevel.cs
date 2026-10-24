using Obsidian.WorldData.BlockNotifications;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// What redstone behaviors need from a level: committed block reads and writes (from <see cref="IBlockChangeWorld"/>), the game and day
/// clocks, sky light at a loaded position (0 when unloaded, so a query never loads a chunk), persisted block ticks, and deferred
/// simulation work that runs on the level's simulation path.
/// </summary>
internal interface IRedstoneLevel : IBlockChangeWorld
{
    long GameTime { get; }

    long DayTime { get; }

    int GetSkyLight(Vector position);

    /// <summary>Schedules a persisted tick of <paramref name="block"/> at <paramref name="position"/>, deduplicated by block type.</summary>
    void ScheduleBlockTick(Vector position, IBlock block, int delay, Obsidian.API.Blocks.TickPriority priority = Obsidian.API.Blocks.TickPriority.Normal);

    /// <summary>Whether a tick of <paramref name="block"/> is already pending at <paramref name="position"/>.</summary>
    bool IsBlockTickScheduled(Vector position, string block);

    /// <summary>Queues work for the level's simulation path, to run after the current block-tick stage.</summary>
    void EnqueueSimulation(Func<ValueTask> action);

    /// <summary>
    /// Commits a block state. <paramref name="notifyNeighbors"/> false is vanilla's flag 2, which updates clients without
    /// notifying neighbors.
    /// </summary>
    ValueTask WriteAsync(Vector position, IBlock block, bool notifyNeighbors);

    /// <summary>Vanilla's <c>hasAnalogOutputSignal</c> for a block state (containers, cauldrons, ...).</summary>
    bool HasAnalogOutput(IBlock block);

    /// <summary>Vanilla's <c>getAnalogOutputSignal</c> of the block at <paramref name="position"/>, read as the neighbour in <paramref name="direction"/>.</summary>
    int GetAnalogOutput(Vector position, IBlock block, BlockFace direction);
}
