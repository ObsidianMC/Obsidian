using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Registries;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData;

/// <summary>
/// The statics of vanilla's <c>BubbleColumnBlock</c>, shared by the column itself and the blocks that start one (soul
/// sand and magma, which push up and down respectively).
/// </summary>
internal static class BubbleColumns
{
    // Vanilla's CHECK_PERIOD: a column re-checks what's under it this many ticks after a neighbour changes.
    internal const int CheckPeriod = 5;

    // Vanilla's soul sand and magma re-check their water this many ticks after the water above changes.
    private const int SourceCheckDelay = 20;

    // A full water source: vanilla's amount of 8 (of 9) from a source, with no falling flag.
    private const int SourceAmount = 8;

    /// <summary>Vanilla's <c>canExistIn</c>: a column block, or a full water source.</summary>
    internal static bool CanExistIn(IBlock block) =>
        block.Is(BlockIds.BubbleColumn)
        || block.Is(BlockIds.Water) && FluidState.Of(block) is { IsSource: true, Amount: >= SourceAmount };

    /// <summary>Vanilla's <c>canSurvive</c>: soul sand, magma, or a column below.</summary>
    internal static bool CanSurvive(IBlock below) =>
        below.Is(BlockIds.SoulSand) || below.Is(BlockIds.MagmaBlock) || below.Is(BlockIds.BubbleColumn);

    // Vanilla's getColumnState: a column keeps the drag of the column below, soul sand drags up, magma drags down, and
    // anything else leaves water.
    private static IBlock ColumnState(IBlock below)
    {
        if (below.Is(BlockIds.BubbleColumn))
            return below;
        if (below.Is(BlockIds.SoulSand))
            return BlocksRegistry.BubbleColumn.WithProperty("drag", "false");
        if (below.Is(BlockIds.MagmaBlock))
            return BlocksRegistry.BubbleColumn.WithProperty("drag", "true");

        return BlocksRegistry.Water;
    }

    /// <summary>
    /// Vanilla's <c>updateColumn</c>: the water at <paramref name="position"/>, if it can hold a column, becomes the column
    /// the block <paramref name="below"/> gives, and the column continues up through water that can hold it. Changes
    /// go to clients without neighbour updates, as vanilla's flag 2.
    /// </summary>
    internal static void UpdateColumn(IBlockLevel level, Vector position, IBlock state, IBlock below)
    {
        if (!CanExistIn(state))
            return;

        var column = ColumnState(below);
        level.SetBlock(position, column, BlockUpdateFlags.Clients);
        var cursor = position + Vector.Up;
        while (level.GetBlock(cursor) is { } above && CanExistIn(above))
        {
            if (!level.SetBlock(cursor, column, BlockUpdateFlags.Clients))
                return;

            cursor += Vector.Up;
        }
    }

    /// <summary>
    /// Vanilla's <c>SoulSandBlock.tick</c> and <c>MagmaBlock.tick</c>: <c>updateColumn(pos.above(), source)</c>, so the
    /// column starts in the water above the source.
    /// </summary>
    internal static void TickSource(IBlockLevel level, Vector position, IBlock source)
    {
        var above = position + Vector.Up;
        if (level.GetBlock(above) is { } current)
            UpdateColumn(level, above, current, source);
    }

    /// <summary>Vanilla's soul sand and magma <c>updateShape</c>: water arriving above them is checked a moment later.</summary>
    internal static void ScheduleWaterAbove(IBlockLevel level, Vector position, IBlock state, BlockFace direction, IBlock neighbor)
    {
        if (direction == BlockFace.Up && neighbor.Is(BlockIds.Water))
            level.ScheduleTick(position, state, SourceCheckDelay);
    }

    /// <summary>Vanilla's source <c>updateShape</c> during post-processing: water above schedules the column check.</summary>
    internal static void PostProcessSource(Generators.Mojang.Features.WorldGenRegion level, Vector position, IBlock state)
    {
        if (level.GetBlock(position + Vector.Up).Is(BlockIds.Water))
            level.ScheduleBlockTick(position, state, SourceCheckDelay);
    }

    /// <summary>Vanilla's <c>BubbleColumnBlock.updateShape</c>: a column that can't stay, or is above the water it can't hold, is checked a tick later.</summary>
    internal static void ScheduleColumnCheck(IBlockLevel level, Vector position, IBlock state, BlockFace direction, IBlock neighbor)
    {
        var below = level.GetBlock(position + Vector.Down);
        var canSurvive = below is not null && CanSurvive(below);
        if (!canSurvive
            || direction == BlockFace.Down
            || direction == BlockFace.Up && !neighbor.Is(BlockIds.BubbleColumn) && CanExistIn(neighbor))
            level.ScheduleTick(position, state, CheckPeriod);
    }
}

/// <summary>
/// Vanilla's <c>BubbleColumnBlock</c>: its <c>tick</c>, <c>updateShape</c> and <c>entityInside</c> overrides. The
/// column pushes entities in it up (magma's down) and over open water above it.
/// </summary>
internal sealed class BubbleColumnBlockBehavior : IScheduledTickBehavior, IShapeUpdateBehavior, IEntityInsideBehavior
{
    /// <summary>Vanilla's <c>BubbleColumnBlock.tick</c>: updates the column from the block below.</summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (level.GetBlock(position + Vector.Down) is { } below)
            BubbleColumns.UpdateColumn(level, position, block, below);
    }

    /// <summary>Vanilla's <c>BubbleColumnBlock.updateShape</c>: the state is kept, and a check is scheduled when needed.</summary>
    public IBlock? UpdateShape(IBlockLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        BubbleColumns.ScheduleColumnCheck(level, position, state, direction, neighbor);
        return null;
    }

    /// <summary>
    /// Vanilla's <c>BubbleColumnBlock.entityInside</c>: over open water (nothing solid, no fluid, above) the entity gets
    /// the above push, and inside a column the inside push.
    /// </summary>
    public void EntityInside(IBlockLevel level, Vector position, IBlock block, Entities.Entity entity)
    {
        var downwards = block.GetProperty("drag") == "true";
        var above = position + Vector.Up;
        var open = level.GetBlock(above) is { } aboveBlock
            && !BlockCollisionShapes.Get(level.Level, above, aboveBlock).Any()
            && FluidState.Of(aboveBlock).IsEmpty;

        if (open)
            entity.OnAboveBubbleColumn(downwards, position);
        else
            entity.OnInsideBubbleColumn(downwards);
    }
}

/// <summary>Vanilla's <c>SoulSandBlock</c>: the <c>tick</c> and <c>updateShape</c> that start a column above it.</summary>
internal sealed class SoulSandBlockBehavior : IScheduledTickBehavior, IShapeUpdateBehavior, IOnPlaceBehavior, IPostProcessBehavior
{
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random) =>
        BubbleColumns.TickSource(level, position, block);

    /// <summary>Vanilla's <c>onPlace</c>: check for water already above the newly placed block after 20 ticks.</summary>
    public void OnPlace(IBlockLevel level, Vector position, IBlock state, IBlock oldState) =>
        level.ScheduleTick(position, state, 20);

    public IBlock? UpdateShape(IBlockLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        BubbleColumns.ScheduleWaterAbove(level, position, state, direction, neighbor);
        return null;
    }

    public void PostProcess(Generators.Mojang.Features.WorldGenRegion level, Vector position, IBlock state) =>
        BubbleColumns.PostProcessSource(level, position, state);
}

/// <summary>Vanilla's <c>MagmaBlock</c>: the <c>tick</c> and <c>updateShape</c> that start a downward column above it.</summary>
internal sealed class MagmaBlockBehavior : IScheduledTickBehavior, IShapeUpdateBehavior, IOnPlaceBehavior, IPostProcessBehavior
{
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random) =>
        BubbleColumns.TickSource(level, position, block);

    /// <summary>Vanilla's <c>onPlace</c>: check for water already above the newly placed block after 20 ticks.</summary>
    public void OnPlace(IBlockLevel level, Vector position, IBlock state, IBlock oldState) =>
        level.ScheduleTick(position, state, 20);

    public IBlock? UpdateShape(IBlockLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        BubbleColumns.ScheduleWaterAbove(level, position, state, direction, neighbor);
        return null;
    }

    public void PostProcess(Generators.Mojang.Features.WorldGenRegion level, Vector position, IBlock state) =>
        BubbleColumns.PostProcessSource(level, position, state);
}
