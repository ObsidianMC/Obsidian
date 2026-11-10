using Obsidian.API.Blocks;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Features.Trees;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// The shared shape pass: vanilla's <c>updateShape</c> for generation post-processing, trees, templates and
/// placement's derivation, and for the live engines once no live owner or plugin answered.
/// </summary>
/// <remarks>
/// A block's rule is the one of the vanilla class that declares its <c>updateShape</c>, bound by the generator
/// (<see cref="ShapeDispatch.SharedRule"/>); the rules live in <c>WorldData/ShapeRules</c> and beside their owners.
/// Rules only the live engines run (<see cref="ShapeReach.LiveOwner"/>) are left out, and blocks without a rule keep
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
        var view = ViewOf(level);
        foreach (var direction in updateShapeOrder)
        {
            var neighbor = level.GetBlock(position.Offset(direction));
            state = UpdateShape(level, view, state, position, direction, neighbor, scheduleFluidTick: true);
        }

        return state;
    }

    public static void UpdateShapeAtEdge(IWorldGenLevel level, TreeVoxelShape shape, Vector origin)
    {
        var view = ViewOf(level);
        shape.ForAllFaces((face, x, y, z) =>
        {
            var position = origin + (x, y, z);
            var neighborPosition = position.Offset(face);
            var state = level.GetBlock(position);
            var neighbor = level.GetBlock(neighborPosition);

            var updated = UpdateShape(level, view, state, position, face, neighbor, scheduleFluidTick: true);
            if (!updated.IsSameState(state))
                level.SetBlock(position, updated);

            var updatedNeighbor = UpdateShape(level, view, neighbor, neighborPosition, face.Opposite(), updated,
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
    /// The level the rule reads. Views that are an <see cref="IShapeLevel"/> already (the live pipeline's and the fluid
    /// engine's) supply its ticks and light; any other level reads as generation (<see cref="GenerationView"/>).
    /// </param>
    internal static IBlock UpdateShape(
        IWorldGenLevel level,
        IBlock state,
        Vector position,
        BlockFace direction,
        IBlock neighbor,
        bool scheduleFluidTick = true) =>
        UpdateShape(level, ViewOf(level), state, position, direction, neighbor, scheduleFluidTick);

    private static IBlock UpdateShape(
        IWorldGenLevel level,
        IShapeLevel view,
        IBlock state,
        Vector position,
        BlockFace direction,
        IBlock neighbor,
        bool scheduleFluidTick)
    {
        if (scheduleFluidTick)
        {
            ScheduleFluidTick(level, state, position, neighbor);
        }

        if (ShapeDispatch.SharedRule(state.RegistryId) is not IShapeUpdateBehavior rule)
            return state;

        return rule.UpdateShape(view, position, state, direction, position.Offset(direction), neighbor) ?? state;
    }

    /// <summary>
    /// The fluid tick vanilla's <c>updateShape</c> schedules: liquids when they or the changed neighbor are a source, every
    /// waterlogged block, and seagrass, kelp and bubble columns (always full of water).
    /// </summary>
    internal static void ScheduleFluidTick(IWorldGenLevel level, IBlock state, Vector position, IBlock neighbor)
    {
        if (state.IsLiquidBlock())
        {
            if (state.IsFluidSource() || neighbor.IsFluidSource())
                level.ScheduleFluidTick(position);
        }
        else if (state.GetProperty("waterlogged") == "true"
            || state.RegistryId is BlockIds.Seagrass or BlockIds.Kelp or BlockIds.BubbleColumn)
        {
            level.ScheduleFluidTick(position);
        }
    }

    private static IShapeLevel ViewOf(IWorldGenLevel level) => level as IShapeLevel ?? new GenerationView(level);

    /// <summary>
    /// A generating level as shape rules read it. Generation's shape pass has no block ticks: scheduling one does
    /// nothing and none reads as scheduled, and rules don't draw from its random, which decides what generates.
    /// Rules that share generation's survival checks read it as the <see cref="IWorldGenLevel"/> it wraps.
    /// </summary>
    private sealed class GenerationView(IWorldGenLevel level) : IShapeLevel, IWorldGenLevel
    {
        public long Seed => level.Seed;

        public int MinY => level.MinY;

        public int Height => level.Height;

        public int SeaLevel => level.SeaLevel;

        public int UnlitBrightness => level.UnlitBrightness;

        public IRandomSource Random =>
            throw new NotSupportedException("Shape rules don't draw from generation's random.");

        public IBlock GetBlock(Vector position) => level.GetBlock(position);

        public int GetStateId(Vector position) => level.GetStateId(position);

        public bool IsFaceSturdy(Vector position, BlockFace face, SupportType support = SupportType.Full) =>
            ShapeViews.IsFaceSturdy(level.GetBlock(position), face, support);

        public int GetRawBrightness(Vector position) => 15;

        public void ScheduleTick(Vector position, IBlock block, int delay, TickPriority priority = TickPriority.Normal)
        {
        }

        public bool HasScheduledTick(Vector position, IBlock block) => false;

        public bool SetBlock(Vector position, IBlock block) => level.SetBlock(position, block);

        public bool EnsureCanWrite(Vector position) => level.EnsureCanWrite(position);

        public int GetHeight(HeightmapType type, int x, int z) => level.GetHeight(type, x, z);

        public BiomeCodec GetBiome(Vector position) => level.GetBiome(position);

        public void SetBlockEntity(Vector position, IBlockEntity blockEntity) =>
            level.SetBlockEntity(position, blockEntity);

        public IBlockEntity? GetBlockEntity(Vector position) => level.GetBlockEntity(position);

        public void AddEntity(GeneratedEntity entity) => level.AddEntity(entity);

        public void MarkForPostProcessing(Vector position) => level.MarkForPostProcessing(position);

        public void ScheduleFluidTick(Vector position) => level.ScheduleFluidTick(position);

        public bool IsOutsideBuildHeight(int y) => level.IsOutsideBuildHeight(y);
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
