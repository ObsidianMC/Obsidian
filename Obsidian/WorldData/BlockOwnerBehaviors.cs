using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData;

/// <summary>
/// A vanilla class's <c>tick</c> override, as Obsidian runs it on a scheduled tick of a block it owns.
/// </summary>
internal interface IScheduledTickBehavior
{
    /// <summary>Runs the scheduled tick of <paramref name="block"/> at <paramref name="position"/>.</summary>
    void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random);
}

/// <summary>
/// A vanilla class's <c>updateShape</c> override, as Obsidian runs it when a neighbour of a block it owns changes.
/// </summary>
internal interface IShapeUpdateBehavior
{
    /// <returns>The state the block becomes, or null to keep <paramref name="state"/>.</returns>
    IBlock? UpdateShape(IBlockLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor);
}

/// <summary>
/// Obsidian implementations of vanilla classes that override <c>Block.tick</c>. Member names match the Mojang
/// declaring classes dumped as <c>block_tick_classes.json</c>; the block generator binds blocks directly to them
/// (<c>BlocksRegistry.GetTickBehavior</c>).
/// </summary>
internal static class TickBehaviors
{
    internal static IScheduledTickBehavior FarmBlock { get; } = new FarmBlockBehavior();

    internal static IScheduledTickBehavior LeavesBlock { get; } = new LeavesBlockBehavior();

    internal static IScheduledTickBehavior FireBlock { get; } = new FireBlockBehavior();
}

/// <summary>
/// Obsidian implementations of vanilla classes that override <c>BlockBehaviour.updateShape</c>, bound from
/// <c>block_update_shape_classes.json</c> (<c>BlocksRegistry.GetShapeUpdateBehavior</c>).
/// </summary>
internal static class ShapeUpdateBehaviors
{
    internal static IShapeUpdateBehavior FarmBlock { get; } = new FarmBlockBehavior();

    internal static IShapeUpdateBehavior LeavesBlock { get; } = new LeavesBlockBehavior();

    internal static IShapeUpdateBehavior FireBlock { get; } = new FireBlockBehavior();
}
