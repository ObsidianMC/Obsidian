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
    /// <summary>Vanilla's <c>updateShape</c>: schedules a tick at delay 1 when the block can't survive, and keeps its state.</summary>
    public IBlock? UpdateShape(IBlockLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        if (!BlockSurvival.SugarCaneMayStay(level.GetBlock, position))
            level.ScheduleTick(position, state, 1);

        return state;
    }

    /// <summary>Vanilla's <c>tick</c>: a stalk that can't survive breaks and drops itself.</summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (!BlockSurvival.SugarCaneMayStay(level.GetBlock, position))
            level.DestroyBlock(position, dropItems: true);
    }
}

/// <summary>Vanilla <c>CactusBlock</c>: the same survival check and break as sugar cane, from its own class.</summary>
internal sealed class CactusBlockBehavior : IScheduledTickBehavior, IShapeUpdateBehavior
{
    /// <summary>Vanilla's <c>updateShape</c>: schedules a tick at delay 1 when the block can't survive, and keeps its state.</summary>
    public IBlock? UpdateShape(IBlockLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        if (!BlockSurvival.CactusMayStay(level.GetBlock, position))
            level.ScheduleTick(position, state, 1);

        return state;
    }

    /// <summary>Vanilla's <c>tick</c>: a cactus that can't survive breaks and drops itself.</summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (!BlockSurvival.CactusMayStay(level.GetBlock, position))
            level.DestroyBlock(position, dropItems: true);
    }
}

/// <summary>
/// Vanilla <c>BambooSaplingBlock.updateShape</c>: air at once when the sapling can't survive, bamboo when bamboo grows
/// above it, and otherwise the state is kept.
/// </summary>
internal sealed class BambooSaplingBlockBehavior : IShapeUpdateBehavior
{
    public IBlock? UpdateShape(IBlockLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        if (!BlockSurvival.BambooMayStay(level.GetBlock, position))
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
    /// <summary>Vanilla's <c>updateShape</c>: schedules a tick at delay 1 when the stalk can't survive, and keeps its state.</summary>
    public IBlock? UpdateShape(IBlockLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        if (!BlockSurvival.BambooMayStay(level.GetBlock, position))
            level.ScheduleTick(position, state, 1);

        return state;
    }

    /// <summary>Vanilla's <c>tick</c>: a stalk that can't survive breaks and drops itself.</summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (!BlockSurvival.BambooMayStay(level.GetBlock, position))
            level.DestroyBlock(position, dropItems: true);
    }
}

/// <summary>
/// Delayed support checks for kelp heads and stems
/// </summary>
internal sealed class KelpBlockBehavior : IScheduledTickBehavior, IShapeUpdateBehavior
{
    /// <summary>Vanilla's <c>updateShape</c>: schedules a tick at delay 1 when the block can't survive, and keeps its state.</summary>
    public IBlock? UpdateShape(IBlockLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        if (state.RegistryId is not (BlockIds.Kelp or BlockIds.KelpPlant))
            return state;

        if (!BlockSurvival.KelpMayPlaceOn(level.GetBlock(position + Vector.Down)))
            level.ScheduleTick(position, state, 1);

        return state;
    }

    /// <summary>Vanilla's <c>tick</c>: a stalk that can't survive breaks and drops itself.</summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (block.RegistryId is not (BlockIds.Kelp or BlockIds.KelpPlant))
            return;

        if (!BlockSurvival.KelpMayPlaceOn(level.GetBlock(position + Vector.Down)))
            level.DestroyBlock(position, dropItems: true);
    }
}
