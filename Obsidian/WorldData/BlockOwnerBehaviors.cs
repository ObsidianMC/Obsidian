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

    internal static IOnPlaceBehavior FireBlock { get; } = new FireBlockBehavior();

    internal static IOnPlaceBehavior CoralPlantBlock { get; } = new CoralPlantBehavior(onWall: false);

    internal static IOnPlaceBehavior CoralFanBlock { get; } = new CoralPlantBehavior(onWall: false);

    internal static IOnPlaceBehavior CoralWallFanBlock { get; } = new CoralPlantBehavior(onWall: true);

    internal static IOnPlaceBehavior TurtleEggBlock { get; } = new TurtleEggBlockBehavior();

    internal static IOnPlaceBehavior FrogspawnBlock { get; } = new FrogspawnBlockBehavior();

    internal static IOnPlaceBehavior FrostedIceBlock { get; } = new FrostedIceBlockBehavior();
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
    /// <summary>Where the rule runs; see <see cref="ShapeDispatch"/>.</summary>
    ShapeReach Reach => ShapeReach.LiveOwner;

    /// <summary>
    /// For a live owner, the rule the shared pass runs for its blocks (generation, chunk completion and placement's
    /// derivation), or null to leave them alone there. Each live owner states its choice explicitly. Generation's
    /// view has no block ticks or random, so owners generally name a reshape-only rule or themselves if they only
    /// reshape. Bubble columns deliberately reuse their scheduling rule to preserve the old shared pass's behavior:
    /// completion's fluid view forwards their checks to the chunk's saved ticks; generation's view ignores them.
    /// </summary>
    IShapeUpdateBehavior? SharedPassRule { get; }

    /// <returns>The state the block becomes, or null to keep <paramref name="state"/>.</returns>
    IBlock? UpdateShape(
        IShapeLevel level,
        Vector position,
        IBlock state,
        BlockFace direction,
        Vector neighborPosition,
        IBlock neighbor);
}

/// <summary>
/// Obsidian implementations of vanilla classes that override <c>Block.tick</c>. Member names match the Mojang
/// declaring classes dumped as <c>block_tick_classes.json</c>; the block generator binds blocks directly to them
/// (<c>BlocksRegistry.GetTickBehavior</c>).
/// </summary>
internal static class TickBehaviors
{
    internal static IScheduledTickBehavior SpeleothemBlock { get; } = new SulfurSpikeBehavior();

    internal static IScheduledTickBehavior BrushableBlock { get; } = new Brushing.BlockBehavior();

    internal static IScheduledTickBehavior FarmlandBlock { get; } = new FarmlandBlockBehavior();

    internal static IScheduledTickBehavior LeavesBlock { get; } = new LeavesBlockBehavior();

    internal static IScheduledTickBehavior FireBlock { get; } = new FireBlockBehavior();

    internal static IScheduledTickBehavior BubbleColumnBlock { get; } = new BubbleColumnBlockBehavior();

    internal static IScheduledTickBehavior SoulSandBlock { get; } = new SoulSandBlockBehavior();

    internal static IScheduledTickBehavior MagmaBlock { get; } = new MagmaBlockBehavior();

    internal static IScheduledTickBehavior SugarCaneBlock { get; } = new SugarCaneBlockBehavior();

    internal static IScheduledTickBehavior CactusBlock { get; } = new CactusBlockBehavior();

    internal static IScheduledTickBehavior BambooStalkBlock { get; } = new BambooStalkBlockBehavior();

    internal static IScheduledTickBehavior GrowingPlantBlock { get; } = new KelpBlockBehavior();

    internal static IScheduledTickBehavior ComposterBlock { get; } = new ComposterBlockBehavior();

    internal static IScheduledTickBehavior AbstractCauldronBlock { get; } = new CauldronDripTick();

    internal static IScheduledTickBehavior PathBlock { get; } = new PathBlockBehavior();

    internal static IScheduledTickBehavior CoralBlock { get; } = new CoralBlockBehavior();

    internal static IScheduledTickBehavior CoralPlantBlock { get; } = new CoralPlantBehavior(onWall: false);

    internal static IScheduledTickBehavior CoralFanBlock { get; } = new CoralPlantBehavior(onWall: false);

    internal static IScheduledTickBehavior CoralWallFanBlock { get; } = new CoralPlantBehavior(onWall: true);

    internal static IScheduledTickBehavior ChorusPlantBlock { get; } = new ChorusPlantBehavior();

    internal static IScheduledTickBehavior FrogspawnBlock { get; } = new FrogspawnBlockBehavior();

    internal static IScheduledTickBehavior EyeblossomBlock { get; } = new EyeblossomBehavior();

    internal static IScheduledTickBehavior DriedGhastBlock { get; } = new DriedGhastBehavior();

    internal static IScheduledTickBehavior FrostedIceBlock { get; } = new FrostedIceBlockBehavior();
}

/// <summary>
/// Obsidian implementations of vanilla classes that override <c>BlockBehaviour.updateShape</c>, bound from
/// <c>block_update_shape_classes.json</c> (<c>BlocksRegistry.GetShapeUpdateBehavior</c>).
/// </summary>
internal static class ShapeUpdateBehaviors
{
    internal static IShapeUpdateBehavior ObserverBlock { get; } = new Redstone.ObserverShapeBehavior();

    internal static IShapeUpdateBehavior FenceBlock { get; } = new FenceBlockBehavior();

    internal static IShapeUpdateBehavior NoteBlock { get; } = new NoteBlockBehavior();

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

    internal static IShapeUpdateBehavior GrowingPlantHeadBlock { get; } = new KelpBlockBehavior();

    internal static IShapeUpdateBehavior GrowingPlantBodyBlock { get; } = new KelpBlockBehavior();

    internal static IShapeUpdateBehavior IronBarsBlock { get; } = new IronBarsShapeUpdateBehavior();

    internal static IShapeUpdateBehavior PathBlock { get; } = new PathBlockBehavior();

    internal static IShapeUpdateBehavior CoralBlock { get; } = new CoralBlockBehavior();

    internal static IShapeUpdateBehavior CoralPlantBlock { get; } = new CoralPlantBehavior(onWall: false);

    internal static IShapeUpdateBehavior CoralFanBlock { get; } = new CoralPlantBehavior(onWall: false);

    internal static IShapeUpdateBehavior CoralWallFanBlock { get; } = new CoralPlantBehavior(onWall: true);

    internal static IShapeUpdateBehavior ChorusPlantBlock { get; } = new ChorusPlantBehavior();

    // Rules only the shared pass runs (ShapeReach.SharedPass), in WorldData/ShapeRules.
    internal static IShapeUpdateBehavior BaseTorchBlock { get; } = new ShapeRules.StandingTorchShapeRule();

    internal static IShapeUpdateBehavior WallTorchBlock { get; } = new ShapeRules.WallAttachedShapeRule();

    internal static IShapeUpdateBehavior RedstoneWallTorchBlock { get; } = new ShapeRules.WallAttachedShapeRule();

    internal static IShapeUpdateBehavior LadderBlock { get; } = new ShapeRules.WallAttachedShapeRule();

    internal static IShapeUpdateBehavior FaceAttachedHorizontalDirectionalBlock { get; } =
        new ShapeRules.FaceAttachedShapeRule();

    internal static IShapeUpdateBehavior LanternBlock { get; } = new ShapeRules.LanternShapeRule();

    internal static IShapeUpdateBehavior WallBannerBlock { get; } = new ShapeRules.WallMountedShapeRule();

    internal static IShapeUpdateBehavior WallSignBlock { get; } = new ShapeRules.WallMountedShapeRule();

    internal static IShapeUpdateBehavior BannerBlock { get; } = new ShapeRules.StandingMountedShapeRule();

    internal static IShapeUpdateBehavior StandingSignBlock { get; } = new ShapeRules.StandingMountedShapeRule();

    internal static IShapeUpdateBehavior CeilingHangingSignBlock { get; } =
        new ShapeRules.CeilingHangingSignShapeRule();

    internal static IShapeUpdateBehavior WallHangingSignBlock { get; } = new ShapeRules.WallHangingSignShapeRule();

    internal static IShapeUpdateBehavior AttachedStemBlock { get; } = new ShapeRules.AttachedStemShapeRule();

    internal static IShapeUpdateBehavior VegetationBlock { get; } = new ShapeRules.SurvivalShapeRule();

    internal static IShapeUpdateBehavior CarpetBlock { get; } = new ShapeRules.SurvivalShapeRule();

    internal static IShapeUpdateBehavior SeagrassBlock { get; } = new ShapeRules.SurvivalShapeRule();

    internal static IShapeUpdateBehavior SnowLayerBlock { get; } = new ShapeRules.SurvivalShapeRule();

    internal static IShapeUpdateBehavior HangingRootsBlock { get; } = new ShapeRules.SurvivalShapeRule();

    internal static IShapeUpdateBehavior SporeBlossomBlock { get; } = new ShapeRules.SurvivalShapeRule();

    internal static IShapeUpdateBehavior CocoaBlock { get; } = new ShapeRules.SurvivalShapeRule();

    internal static IShapeUpdateBehavior HangingMossBlock { get; } = new ShapeRules.HangingMossShapeRule();

    internal static IShapeUpdateBehavior MossyCarpetBlock { get; } = new ShapeRules.MossyCarpetShapeRule();

    internal static IShapeUpdateBehavior DoorBlock { get; } = new ShapeRules.DoorShapeRule();

    internal static IShapeUpdateBehavior AbstractBedBlock { get; } = new ShapeRules.BedShapeRule();

    internal static IShapeUpdateBehavior ChestBlock { get; } = new ShapeRules.ChestShapeRule();

    internal static IShapeUpdateBehavior DoublePlantBlock { get; } = new ShapeRules.DoublePlantShapeRule();

    internal static IShapeUpdateBehavior SmallDripleafBlock { get; } = new ShapeRules.DoublePlantShapeRule();

    internal static IShapeUpdateBehavior WallBlock { get; } = new ShapeRules.WallShapeRule();

    internal static IShapeUpdateBehavior FenceGateBlock { get; } = new ShapeRules.FenceGateShapeRule();

    internal static IShapeUpdateBehavior StairBlock { get; } = new ShapeRules.StairShapeRule();

    internal static IShapeUpdateBehavior VineBlock { get; } = new ShapeRules.VineShapeRule();

    internal static IShapeUpdateBehavior RedstoneWireBlock { get; } = new ShapeRules.RedstoneWireShapeRule();

    internal static IShapeUpdateBehavior SnowyBlock { get; } = new ShapeRules.SnowyShapeRule();
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
