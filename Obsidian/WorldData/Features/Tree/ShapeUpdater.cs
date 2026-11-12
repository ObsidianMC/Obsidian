using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// The shared shape pass: vanilla's <c>updateShape</c> for generation post-processing, trees, templates and
/// placement's derivation, and for a live level's shape update once no live owner or plugin answered.
/// </summary>
/// <remarks>
/// A block's rule is the one of the vanilla class that declares its <c>updateShape</c>, bound by the generator
/// (<see cref="ShapeDispatch.SharedRule"/>); the rules live in <c>WorldData/ShapeRules</c> and beside their owners.
/// Rules only a live level runs (<see cref="ShapeReach.LiveOwner"/>) are left out, and blocks without a rule keep
/// their state; the vanilla classes still missing one are tracked in the known gaps.
/// </remarks>
internal static class ShapeUpdater
{
    // Vanilla BlockBehaviour.UPDATE_SHAPE_ORDER.
    private static readonly BlockFace[] updateShapeOrder = [BlockFace.West, BlockFace.East, BlockFace.North,
        BlockFace.South, BlockFace.Down, BlockFace.Up];

    /// <summary>
    /// Vanilla <c>Block.updateFromNeighbourShapes</c>: <paramref name="state"/> updated against each of its neighbors.
    /// </summary>
    public static IBlock UpdateFromNeighbourShapes(IWorldGenLevel level, IBlock state, Vector position)
    {
        foreach (var direction in updateShapeOrder)
        {
            var neighbor = level.GetBlock(position.Offset(direction));
            state = UpdateShape(level, state, position, direction, neighbor, scheduleFluidTick: true);
        }

        return state;
    }

    public static void UpdateShapeAtEdge(IWorldGenLevel level, TreeVoxelShape shape, Vector origin)
    {
        shape.ForAllFaces((face, x, y, z) =>
        {
            var position = origin + (x, y, z);
            var neighborPosition = position.Offset(face);
            var state = level.GetBlock(position);
            var neighbor = level.GetBlock(neighborPosition);

            var updated = UpdateShape(level, state, position, face, neighbor, scheduleFluidTick: true);
            if (!updated.IsSameState(state))
                level.SetBlock(position, updated);

            var updatedNeighbor = UpdateShape(level, neighbor, neighborPosition, face.Opposite(), updated,
                scheduleFluidTick: true);
            if (!updatedNeighbor.IsSameState(neighbor))
                level.SetBlock(neighborPosition, updatedNeighbor);
        });
    }

    /// <summary>
    /// Vanilla <c>updateShape</c>: the state of <paramref name="state"/> after its neighbor in <paramref name="direction"/>
    /// became <paramref name="neighbor"/>, scheduling the fluid tick vanilla schedules there.
    /// </summary>
    /// <param name="level">
    /// The level the rule reads. Views that are an <see cref="IShapeLevel"/> already (the fluid engine's, and the
    /// pipeline's where it's the only engine) supply its ticks and light; any other level reads as generation (<see cref="GenerationView"/>).
    /// </param>
    internal static IBlock UpdateShape(
        IWorldGenLevel level,
        IBlock state,
        Vector position,
        BlockFace direction,
        IBlock neighbor,
        bool scheduleFluidTick = true)
    {
        if (scheduleFluidTick)
        {
            ScheduleFluidTick(level, state, position, neighbor);
        }

        if (ShapeDispatch.SharedRule(state.RegistryId) is not IShapeUpdateBehavior rule)
            return state;

        var neighborPosition = position.Offset(direction);
        if (level is IShapeLevel view)
            return rule.UpdateShape(view, position, state, direction, neighborPosition, neighbor) ?? state;

        // Reuse generation's view without retaining the level after a failure. Nested updates must restore the
        // outer rule's level before it continues reading.
        var generation = generationView ??= new GenerationView();
        var previousLevel = generation.Level;
        generation.Level = level;
        try
        {
            return rule.UpdateShape(generation, position, state, direction, neighborPosition, neighbor) ?? state;
        }
        finally
        {
            generation.Level = previousLevel;
        }
    }

    /// <summary>
    /// The fluid tick vanilla's <c>updateShape</c> schedules: liquids when they or the changed neighbor are a source, every
    /// waterlogged block, and seagrass, kelp (heads and stems) and bubble columns (always full of water).
    /// </summary>
    internal static void ScheduleFluidTick(IWorldGenLevel level, IBlock state, Vector position, IBlock neighbor)
    {
        if (state.IsLiquidBlock())
        {
            if (state.IsFluidSource() || neighbor.IsFluidSource())
                level.ScheduleFluidTick(position);
        }
        else if (PropertyHandles.Waterlogged.IsTrue(state)
            || state.RegistryId is BlockIds.Seagrass or BlockIds.Kelp or BlockIds.KelpPlant or BlockIds.BubbleColumn)
        {
            level.ScheduleFluidTick(position);
        }
    }

    [ThreadStatic]
    private static GenerationView? generationView;

    /// <summary>
    /// A generating level as shape rules read it. Generation's shape pass has no block ticks: scheduling one does
    /// nothing and none reads as scheduled, and rules don't draw from its random, which decides what generates.
    /// </summary>
    private sealed class GenerationView : IShapeLevel
    {
        /// <summary>The level the current rule reads; null between rules.</summary>
        public IWorldGenLevel? Level { get; set; }

        public IRandomSource Random =>
            throw new NotSupportedException("Shape rules don't draw from generation's random.");

        public IBlock GetBlock(Vector position) => this.Level!.GetBlock(position);

        public bool IsFaceSturdy(Vector position, BlockFace face, SupportType support = SupportType.Full) =>
            ShapeViews.IsFaceSturdy(this.Level!.GetBlock(position), face, support);

        public IBlockEntity? GetBlockEntity(Vector position) => this.Level!.GetBlockEntity(position);

        public int GetRawBrightness(Vector position) => 15;

        public void ScheduleTick(
            Vector position,
            IBlock block,
            int delay,
            TickPriority priority = TickPriority.Normal)
        {
        }

        public bool HasScheduledTick(Vector position, IBlock block) => false;
    }
}

/// <summary>Helpers the shared pass's views share.</summary>
internal static class ShapeViews
{
    /// <summary>
    /// Whether <paramref name="block"/>'s face is sturdy, from its state alone (the shared pass reads no block entity
    /// shapes).
    /// </summary>
    public static bool IsFaceSturdy(IBlock block, BlockFace face, SupportType support) =>
        support == SupportType.Full ? block.IsFaceSturdy(face) : block.IsFaceSturdy(face, support);
}
