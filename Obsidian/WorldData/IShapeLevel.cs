using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData;

/// <summary>
/// The level as a shape rule (vanilla's <c>updateShape</c>) sees it. A rule reads blocks, may schedule a tick or draw
/// from the level's random, and returns the state its block becomes; it never writes, so the caller commits the
/// state. The live level (<see cref="LevelBlockUpdates"/>), the notification pipeline's and fluid engine's views,
/// and world generation all supply one, so each rule has one implementation for all of them.
/// </summary>
/// <remarks>
/// Views that can't do something say so: generation has no block ticks to schedule or read (scheduling does nothing
/// and nothing reads as scheduled), and only the live level has a level random to draw from. Survival validation
/// drops scheduling and uses a private random, so evaluating a rule cannot shift the level's random sequence.
/// </remarks>
internal interface IShapeLevel
{
    /// <summary>The block at <paramref name="position"/>.</summary>
    IBlock GetBlock(Vector position);

    /// <summary>Whether the block's face supports the requested area, like vanilla's <c>isFaceSturdy</c>.</summary>
    bool IsFaceSturdy(Vector position, BlockFace face, SupportType support = SupportType.Full);

    /// <summary>The block entity at <paramref name="position"/>, or null.</summary>
    IBlockEntity? GetBlockEntity(Vector position);

    /// <summary>The level's random, which vanilla's rules draw from.</summary>
    IRandomSource Random { get; }

    /// <summary>
    /// The raw brightness at <paramref name="position"/>, which only mushrooms' survival reads. Generation reads 15.
    /// </summary>
    int GetRawBrightness(Vector position);

    /// <summary>Schedules a tick of <paramref name="block"/>'s block, like vanilla's <c>scheduleTick</c>.</summary>
    void ScheduleTick(Vector position, IBlock block, int delay, TickPriority priority = TickPriority.Normal);

    /// <summary>
    /// Whether a tick of <paramref name="block"/>'s block is scheduled at <paramref name="position"/>.
    /// </summary>
    bool HasScheduledTick(Vector position, IBlock block);
}

/// <summary>Where a shape behaviour (<see cref="IShapeUpdateBehavior"/>) runs.</summary>
internal enum ShapeReach
{
    /// <summary>
    /// Ahead of everything else in a live level's shape updates (<see cref="LevelBlockUpdates.UpdateShape"/>), which
    /// only the fluid level's vanilla-ordered updater runs: the notification pipeline runs shape steps only where it's
    /// the only engine (a test world). The shared pass (<see cref="Features.Tree.ShapeUpdater"/>) runs the owner's
    /// <see cref="IShapeUpdateBehavior.SharedPassRule"/> instead, if it names one.
    /// </summary>
    LiveOwner,

    /// <summary>
    /// Only in the shared pass: generation, placement's derivation, and a live level's fallback once no live owner or
    /// plugin answered. On a live level the fluid level's updater runs that fallback for every change, players' and
    /// plugins' as well as fluid ticks'.
    /// </summary>
    SharedPass
}

/// <summary>
/// Reads an <see cref="IBlockLevel"/> as the view shape rules take, for code that shares helpers with a rule.
/// </summary>
internal static class ShapeLevels
{
    /// <summary>
    /// <paramref name="level"/> as an <see cref="IShapeLevel"/>: the live level is one already, and any other block
    /// level is wrapped.
    /// </summary>
    public static IShapeLevel AsShapeLevel(this IBlockLevel level) =>
        level as IShapeLevel ?? new BlockLevelView(level);

    private sealed class BlockLevelView(IBlockLevel level) : IShapeLevel
    {
        public IRandomSource Random => level.Random;

        public IBlock GetBlock(Vector position) => level.GetBlock(position);

        public bool IsFaceSturdy(Vector position, BlockFace face, SupportType support = SupportType.Full) =>
            level.IsFaceSturdy(position, face, support);

        public IBlockEntity? GetBlockEntity(Vector position) => level.GetBlockEntity(position);

        public int GetRawBrightness(Vector position) =>
            level.Level is AbstractLevel live ? live.Fluids.GetRawBrightness(position) : 15;

        public void ScheduleTick(
            Vector position,
            IBlock block,
            int delay,
            TickPriority priority = TickPriority.Normal) =>
            level.ScheduleTick(position, block, delay, priority);

        public bool HasScheduledTick(Vector position, IBlock block) => level.HasScheduledTick(position, block);
    }
}
