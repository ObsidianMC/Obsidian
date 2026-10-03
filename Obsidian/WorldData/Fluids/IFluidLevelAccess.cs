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
    /// Stores a block without any side effects besides keeping its chunk consistent (heightmaps, block entities, clients).
    /// </summary>
    /// <returns>Whether the block could be stored; <c>false</c> outside the build range or the reachable chunks.</returns>
    public bool SetBlock(Vector position, IBlock block);

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
}
