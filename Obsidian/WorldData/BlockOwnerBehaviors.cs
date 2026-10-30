using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;

namespace Obsidian.WorldData;

/// <summary>
/// A vanilla class's <c>tick</c> override, as Obsidian runs it on a scheduled tick of a block it owns.
/// </summary>
internal interface IScheduledTickBehavior
{
    /// <summary>Runs the scheduled tick of <paramref name="block"/> at <paramref name="position"/>.</summary>
    void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random);
}

/// <summary>A vanilla class's <c>onPlace</c> override, run after one of its blocks replaces another state.</summary>
internal interface IOnPlaceBehavior
{
    void OnPlace(IBlockLevel level, Vector position, IBlock state, IBlock oldState);
}

/// <summary>
/// Obsidian implementations of vanilla classes that override <c>onPlace</c>, bound from
/// <c>block_on_place_classes.json</c> (<c>BlocksRegistry.GetOnPlaceBehavior</c>).
/// </summary>
internal static class OnPlaceBehaviors
{
    internal static IOnPlaceBehavior PotentSulfurBlock { get; } = new PotentSulfurBlockBehavior();

    internal static IOnPlaceBehavior SoulSandBlock { get; } = new SoulSandBlockBehavior();

    internal static IOnPlaceBehavior MagmaBlock { get; } = new MagmaBlockBehavior();

    internal static IOnPlaceBehavior ComposterBlock { get; } = new ComposterBlockBehavior();
}

/// <summary>
/// A vanilla class's <c>updateShape</c> override, as Obsidian runs it when a neighbour of a block it owns changes.
/// </summary>
/// <summary>
/// A vanilla class's <c>updateShape</c> as worldgen post-processing runs it: its scheduled checks go to the chunk's
/// block ticks, since a generating region has no running level.
/// </summary>
internal interface IPostProcessBehavior
{
    void PostProcess(Generators.Mojang.Features.WorldGenRegion level, Vector position, IBlock state);
}

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
    internal static IScheduledTickBehavior SpeleothemBlock { get; } = new SulfurSpikeBehavior();

    internal static IScheduledTickBehavior FarmlandBlock { get; } = new FarmlandBlockBehavior();

    internal static IScheduledTickBehavior LeavesBlock { get; } = new LeavesBlockBehavior();

    internal static IScheduledTickBehavior FireBlock { get; } = new FireBlockBehavior();

    internal static IScheduledTickBehavior BubbleColumnBlock { get; } = new BubbleColumnBlockBehavior();

    internal static IScheduledTickBehavior SoulSandBlock { get; } = new SoulSandBlockBehavior();

    internal static IScheduledTickBehavior MagmaBlock { get; } = new MagmaBlockBehavior();

    internal static IScheduledTickBehavior SugarCaneBlock { get; } = new SugarCaneBlockBehavior();

    internal static IScheduledTickBehavior CactusBlock { get; } = new CactusBlockBehavior();

    internal static IScheduledTickBehavior BambooStalkBlock { get; } = new BambooStalkBlockBehavior();

    internal static IScheduledTickBehavior ComposterBlock { get; } = new ComposterBlockBehavior();
}

/// <summary>
/// Obsidian implementations of vanilla classes that override <c>BlockBehaviour.updateShape</c>, bound from
/// <c>block_update_shape_classes.json</c> (<c>BlocksRegistry.GetShapeUpdateBehavior</c>).
/// </summary>
internal static class ShapeUpdateBehaviors
{
    internal static IShapeUpdateBehavior SpeleothemBlock { get; } = new SulfurSpikeBehavior();

    internal static IShapeUpdateBehavior PotentSulfurBlock { get; } = new PotentSulfurBlockBehavior();

    internal static IShapeUpdateBehavior FarmlandBlock { get; } = new FarmlandBlockBehavior();

    internal static IShapeUpdateBehavior LeavesBlock { get; } = new LeavesBlockBehavior();

    internal static IShapeUpdateBehavior FireBlock { get; } = new FireBlockBehavior();

    internal static IShapeUpdateBehavior BubbleColumnBlock { get; } = new BubbleColumnBlockBehavior();

    internal static IShapeUpdateBehavior SoulSandBlock { get; } = new SoulSandBlockBehavior();

    internal static IShapeUpdateBehavior MagmaBlock { get; } = new MagmaBlockBehavior();

    internal static IShapeUpdateBehavior SugarCaneBlock { get; } = new SugarCaneBlockBehavior();

    internal static IShapeUpdateBehavior CactusBlock { get; } = new CactusBlockBehavior();

    internal static IShapeUpdateBehavior BambooStalkBlock { get; } = new BambooStalkBlockBehavior();

    internal static IShapeUpdateBehavior BambooSaplingBlock { get; } = new BambooSaplingBlockBehavior();

    internal static IShapeUpdateBehavior IronBarsBlock { get; } = new IronBarsShapeUpdateBehavior();
}

/// <summary>
/// A vanilla class's <c>entityInside</c> override, run for an entity whose box overlaps a block it owns.
/// </summary>
internal interface IEntityInsideBehavior
{
    /// <summary>Runs the contact of <paramref name="entity"/> with <paramref name="block"/> at <paramref name="position"/>.</summary>
    void EntityInside(IBlockLevel level, Vector position, IBlock block, Entities.Entity entity);
}

/// <summary>
/// Obsidian implementations of vanilla classes that override <c>entityInside</c>, bound from
/// <c>block_entity_inside_classes.json</c> (<c>BlocksRegistry.GetEntityInsideBehavior</c>).
/// </summary>
internal static class EntityInsideBehaviors
{
    internal static IEntityInsideBehavior BubbleColumnBlock { get; } = new BubbleColumnBlockBehavior();
}
