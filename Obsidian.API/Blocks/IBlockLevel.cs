using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.Blocks;

/// <summary>
/// The level as a block's hooks see it, like the level vanilla passes its blocks: reads and changes blocks at once,
/// with the level's block updates held for the hook, so what it reads stays as it read it until it returns.
/// </summary>
/// <remarks>
/// Only use it during the hook it's given to, and use it rather than <see cref="ILevel"/>'s asynchronous methods,
/// which wait for the hook to return. It reaches the level's complete loaded chunks: others read as void air and
/// can't be changed. Its redstone signals (see <see cref="ISignalReader"/>) are read as the blocks are now.
/// </remarks>
public interface IBlockLevel : ISignalReader
{
    /// <summary>
    /// The level.
    /// </summary>
    public ILevel Level { get; }

    /// <summary>
    /// The level's random, which vanilla's blocks tick with.
    /// </summary>
    public IRandomSource Random { get; }

    /// <summary>
    /// The block at <paramref name="position"/>: void air outside the build range or the loaded chunks.
    /// </summary>
    public IBlock GetBlock(Vector position);

    /// <summary>
    /// The block entity at <paramref name="position"/>, like vanilla's <c>getBlockEntity</c>: what a block's hooks read
    /// their own data from, a comparator output from a counter, say. Null outside the loaded chunks.
    /// </summary>
    /// <remarks>
    /// Hooks can run while block entities tick (a player's change runs on its own thread), so read what is safe to read
    /// so, like a field the block entity writes whole, and change the block entity on its own tick.
    /// </remarks>
    public IBlockEntity? GetBlockEntity(Vector position);

    /// <summary>
    /// Changes the block at <paramref name="position"/>, with the side effects <paramref name="flags"/> ask for. The
    /// neighbour and shape updates it causes run after the current hook's, like vanilla's chained updates.
    /// </summary>
    /// <returns>Whether the block changed.</returns>
    public bool SetBlock(Vector position, IBlock block, BlockUpdateFlags flags = BlockUpdateFlags.All);

    /// <summary>
    /// Removes the block at <paramref name="position"/>, leaving the fluid it held (air, or water for a waterlogged
    /// block), with <see cref="BlockUpdateFlags.All"/>, like vanilla's <c>removeBlock</c>. It drops nothing.
    /// </summary>
    /// <returns>Whether the block changed.</returns>
    public bool RemoveBlock(Vector position);

    /// <summary>
    /// Schedules a tick of <paramref name="block"/>'s block at <paramref name="position"/> in <paramref name="delay"/>
    /// game ticks, unless one is scheduled there already, like vanilla's <c>scheduleTick</c>. It runs if that block is
    /// still there then.
    /// </summary>
    public void ScheduleTick(Vector position, IBlock block, int delay, TickPriority priority = TickPriority.Normal);

    /// <summary>
    /// Whether a tick of <paramref name="block"/>'s block is scheduled at <paramref name="position"/>.
    /// </summary>
    public bool HasScheduledTick(Vector position, IBlock block);

    /// <summary>
    /// Tells the six neighbours of <paramref name="position"/> that it changed, from <paramref name="source"/>, like
    /// vanilla's <c>updateNeighborsAt</c>.
    /// </summary>
    public void UpdateNeighbors(Vector position, IBlock source);

    /// <summary>
    /// Tells the comparators reading the block at <paramref name="position"/> that its analog output (see
    /// <see cref="IBlockBehavior.GetAnalogOutput"/>) changed, like vanilla's <c>updateNeighbourForOutputSignal</c>:
    /// those beside it and those behind a conductor beside it. A block change with neighbour updates does this for a
    /// block with an analog output already.
    /// </summary>
    public void UpdateAnalogOutput(Vector position);
}
