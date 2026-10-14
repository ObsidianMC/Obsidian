using Obsidian.API.Blocks;

namespace Obsidian.WorldData.Fluids;

/// <summary>
/// The blocks and tick lists a <see cref="FluidLevel"/> works on: the live level's loaded chunks, or the chunks around a
/// chunk being post-processed during generation.
/// </summary>
internal interface IFluidLevelAccess
{
    /// <summary>
    /// The lowest block Y of the build range.
    /// </summary>
    public int MinY { get; }

    /// <summary>
    /// The height of the build range in blocks.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// The dimension setting and game rules fluids follow.
    /// </summary>
    public FluidRules Rules { get; }

    /// <summary>
    /// The level's random (vanilla's <c>Level.random</c>), which lava's spread delay draws from.
    /// </summary>
    public Random Random { get; }

    /// <summary>
    /// The block at <paramref name="position"/>; void air outside the build range, like vanilla.
    /// </summary>
    public IBlock GetBlock(Vector position);

    /// <summary>
    /// Stores a block without any side effects besides keeping its chunk consistent (heightmaps, block entities):
    /// players and portals hear of it through <see cref="OnBlockStored"/>, as the change's flags ask.
    /// </summary>
    /// <returns>Whether the block could be stored; <c>false</c> outside the build range or the reachable chunks.</returns>
    public bool SetBlock(Vector position, IBlock block);

    /// <summary>
    /// A block was stored with <paramref name="flags"/>, before its updates run: the live level shows it to players
    /// (<see cref="BlockUpdateFlags.Clients"/>) and has portals check their frames
    /// (<see cref="BlockUpdateFlags.Neighbors"/>).
    /// </summary>
    public void OnBlockStored(Vector position, IBlock block, BlockUpdateFlags flags)
    {
    }

    /// <summary>
    /// Schedules a tick of <paramref name="fluid"/> at <paramref name="position"/> in <paramref name="delay"/> ticks, in the
    /// tick list of the position's chunk (ignored when one is already scheduled there for that fluid).
    /// </summary>
    public void ScheduleFluidTick(Vector position, FluidKind fluid, int delay);

    /// <summary>
    /// Vanilla <c>levelEvent</c> (sounds and particles, like 1501 for lava fizzing).
    /// </summary>
    public void LevelEvent(int type, Vector position, int data)
    {
    }

    /// <summary>
    /// How blocks other than liquids react to block updates, or null for only the vanilla blocks Obsidian models.
    /// </summary>
    public IBlockReactions? Reactions => null;
}

/// <summary>
/// How blocks other than liquids react to block updates on a live level, beyond vanilla's shape rules: plugin blocks'
/// hooks, and vanilla's falling blocks scheduling their tick. Each runs while the level's block updates wait for it.
/// </summary>
internal interface IBlockReactions
{
    /// <summary>
    /// Vanilla <c>affectNeighborsAfterRemoval</c>: <paramref name="state"/> was replaced by another block.
    /// </summary>
    public void OnRemoved(Vector position, IBlock state, IBlock newState);

    /// <summary>
    /// Vanilla <c>onPlace</c>: <paramref name="state"/> replaced <paramref name="oldState"/>.
    /// </summary>
    public void OnPlaced(Vector position, IBlock state, IBlock oldState);

    /// <summary>
    /// Vanilla <c>neighborChanged</c>.
    /// </summary>
    public void NeighborChanged(Vector position, IBlock state, IBlock source, Vector sourcePosition);

    /// <summary>
    /// Vanilla <c>updateShape</c> for the blocks this handles.
    /// </summary>
    /// <returns>The block's new state, or null to leave it to vanilla's shape rules.</returns>
    public IBlock? UpdateShape(
        Vector position,
        IBlock state,
        BlockFace direction,
        Vector neighborPosition,
        IBlock neighbor);
}
