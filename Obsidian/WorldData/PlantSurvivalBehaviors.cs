using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla <c>SugarCaneBlock</c>: a neighbour change schedules a survival check, and the check breaks the stalk when it
/// can't stay, so the stalks above it fall as their own checks fail in turn.
/// </summary>
internal sealed class SugarCaneBlockBehavior : IScheduledTickBehavior, IShapeUpdateBehavior
{
    public IShapeUpdateBehavior? SharedPassRule => null;

    /// <summary>Vanilla's <c>updateShape</c>: schedules a tick at delay 1 when the block can't survive, and keeps its state.</summary>
    public IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        if (!BlockSurvival.SugarCaneMayStay(new BlockReader(level), position))
            level.ScheduleTick(position, state, 1);

        return state;
    }

    /// <summary>Vanilla's <c>tick</c>: a stalk that can't survive breaks and drops itself.</summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (!BlockSurvival.SugarCaneMayStay(new BlockReader(level.AsShapeLevel()), position))
            level.DestroyBlock(position, dropItems: true);
    }
}

/// <summary>Vanilla <c>CactusBlock</c>: the same survival check and break as sugar cane, from its own class.</summary>
internal sealed class CactusBlockBehavior : IScheduledTickBehavior, IShapeUpdateBehavior
{
    public IShapeUpdateBehavior? SharedPassRule => null;

    /// <summary>Vanilla's <c>updateShape</c>: schedules a tick at delay 1 when the block can't survive, and keeps its state.</summary>
    public IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        if (!BlockSurvival.CactusMayStay(new BlockReader(level), position))
            level.ScheduleTick(position, state, 1);

        return state;
    }

    /// <summary>Vanilla's <c>tick</c>: a cactus that can't survive breaks and drops itself.</summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (!BlockSurvival.CactusMayStay(new BlockReader(level.AsShapeLevel()), position))
            level.DestroyBlock(position, dropItems: true);
    }
}

/// <summary>
/// Vanilla <c>BambooSaplingBlock.updateShape</c>: air at once when the sapling can't survive, bamboo when bamboo grows
/// above it, and otherwise the state is kept.
/// </summary>
internal sealed class BambooSaplingBlockBehavior : IShapeUpdateBehavior
{
    public IShapeUpdateBehavior? SharedPassRule => null;

    public IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        if (!BlockSurvival.BambooMayStay(new BlockReader(level), position))
            return BlocksRegistry.Air;

        return direction == BlockFace.Up && neighbor.Is(BlockIds.Bamboo)
            ? BlocksRegistry.GetDefault(BlockIds.Bamboo)
            : state;
    }
}

/// <summary>
/// Vanilla <c>BambooStalkBlock</c>: the same delayed survival check and break as sugar cane, on the bamboo-plantable
/// tag beneath it.
/// </summary>
internal sealed class BambooStalkBlockBehavior : IScheduledTickBehavior, IShapeUpdateBehavior
{
    public IShapeUpdateBehavior? SharedPassRule => null;

    /// <summary>Vanilla's <c>updateShape</c>: schedules a tick at delay 1 when the stalk can't survive, and keeps its state.</summary>
    public IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        if (!BlockSurvival.BambooMayStay(new BlockReader(level), position))
            level.ScheduleTick(position, state, 1);

        return state;
    }

    /// <summary>Vanilla's <c>tick</c>: a stalk that can't survive breaks and drops itself.</summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (!BlockSurvival.BambooMayStay(new BlockReader(level.AsShapeLevel()), position))
            level.DestroyBlock(position, dropItems: true);
    }
}

/// <summary>
/// The shape updates and support checks of kelp and the cave, weeping and twisting vines, heads and bodies. Only kelp
/// breaks when its support check finds it can't stay.
/// </summary>
internal sealed class KelpBlockBehavior : IScheduledTickBehavior, IShapeUpdateBehavior, IPostProcessBehavior
{
    private static readonly IShapeUpdateBehavior sharedPassRule = new KelpShapeRule();

    /// <summary>
    /// The shared pass only turns a kelp head with kelp above into a stem, as its copy of the rule always did, and
    /// schedules nothing: chunk completion schedules the support check (see <see cref="PostProcess"/>). The cave,
    /// weeping and twisting vines bound to the same classes keep their state there.
    /// </summary>
    public IShapeUpdateBehavior? SharedPassRule => sharedPassRule;

    /// <summary>
    /// Chunk completion (vanilla's <c>LevelChunk.postProcessGeneration</c>) runs the rule against the live level, so a
    /// plant that lost its support during generation checks it a tick after the chunk is full: after the shared pass,
    /// the update from the support side schedules that in the chunk's ticks.
    /// </summary>
    public void PostProcess(Generators.Mojang.Features.WorldGenRegion level, Vector position, IBlock state)
    {
        var current = level.GetBlock(position);
        foreach (var side in (ReadOnlySpan<BlockFace>)[BlockFace.Down, BlockFace.Up])
        {
            if (GrowingPlantShapes.NeedsSupportCheck(current, side, level.GetBlock(position.Offset(side))))
                level.ScheduleBlockTick(position, current, 1);
        }
    }

    /// <summary>
    /// Vanilla's <c>updateShape</c> (see <see cref="GrowingPlantShapes"/>): an update from the support side schedules
    /// the support check a tick later when the plant can't stay, and a head or body turns into the plant's other part
    /// when its plant ahead appears or goes. Other states are kept.
    /// </summary>
    public IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        if (GrowingPlantShapes.NeedsSupportCheck(state, direction, neighbor))
            level.ScheduleTick(position, state, 1);

        var ahead = GrowingPlantShapes.BlockAheadToRead(position, state, direction) is Vector at
            ? level.GetBlock(at)
            : null;

        return GrowingPlantShapes.Converts(state, direction, neighbor, ahead)
            ? GrowingPlantShapes.Convert(state, level.Random)
            : state;
    }

    /// <summary>Vanilla's <c>tick</c>: a stalk that can't survive breaks and drops itself.</summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (block.RegistryId is not (BlockIds.Kelp or BlockIds.KelpPlant))
            return;

        if (!BlockSurvival.KelpMayPlaceOn(level.GetBlock(position + Vector.Down)))
            level.DestroyBlock(position, dropItems: true);
    }

    /// <summary>The live rule's reshaping without its support check (see <see cref="SharedPassRule"/>).</summary>
    private sealed class KelpShapeRule : ShapeRules.SharedShapeRule
    {
        public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
            Vector neighborPosition, IBlock neighbor)
        {
            var turnsIntoStem = state.RegistryId == BlockIds.Kelp
                && direction == BlockFace.Up
                && neighbor.RegistryId is BlockIds.Kelp or BlockIds.KelpPlant;
            return turnsIntoStem ? BlocksRegistry.KelpPlant : state;
        }
    }
}
