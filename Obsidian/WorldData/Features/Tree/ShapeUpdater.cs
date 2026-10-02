namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// Neighbor shape updates during generation: vanilla <c>StructureTemplate.updateShapeAtEdge</c> after tree and template
/// placement, and <c>Block.updateFromNeighbourShapes</c> for blocks marked for post-processing.
/// </summary>
/// <remarks>
/// Obsidian has no general <c>updateShape</c> implementation, so only the block behaviors that matter for generated
/// blocks are reproduced: vines re-checking their supports, double plants losing their other half, snowy dirt
/// (<c>snowy</c> from the block above), hanging moss (<c>tip</c>), liquids scheduling a fluid tick, fence and bar
/// connections, and torches and ladders dropping without support. Other blocks keep their state, which matches vanilla for
/// logs, leaves (they only schedule ticks), roots and most plants.
/// </remarks>
internal static class ShapeUpdater
{
    private static readonly IBlock air = BlockStateProperties.GetState("minecraft:air");

    private static readonly BlockSet farmland = new("minecraft:farmland");

    private static readonly BlockSet fences = new("#minecraft:fences");

    private static readonly BlockSet woodenFences = new("#minecraft:wooden_fences");

    private static readonly BlockSet walls = new("#minecraft:walls");

    // Blocks fences and bars never connect to for having a sturdy face (Block.isExceptionForConnection, besides leaves).
    private static readonly BlockSet connectionExceptions = new("minecraft:barrier", "minecraft:carved_pumpkin", "minecraft:jack_o_lantern",
        "minecraft:melon", "minecraft:pumpkin", "#minecraft:shulker_boxes");

    // Vanilla BlockBehaviour.UPDATE_SHAPE_ORDER.
    private static readonly BlockFace[] updateShapeOrder = [BlockFace.West, BlockFace.East, BlockFace.North, BlockFace.South, BlockFace.Down, BlockFace.Up];

    /// <summary>
    /// Vanilla <c>Block.updateFromNeighbourShapes</c>: <paramref name="state"/> updated against each of its neighbors.
    /// </summary>
    public static IBlock UpdateFromNeighbourShapes(IWorldGenLevel level, IBlock state, Vector position)
    {
        foreach (var direction in updateShapeOrder)
            state = UpdateShape(level, state, position, direction, level.GetBlock(position + direction.ToVector()));

        return state;
    }

    public static void UpdateShapeAtEdge(IWorldGenLevel level, TreeVoxelShape shape, Vector origin)
    {
        shape.ForAllFaces((face, x, y, z) =>
        {
            var position = origin + (x, y, z);
            var neighborPosition = position + face.ToVector();
            var state = level.GetBlock(position);
            var neighbor = level.GetBlock(neighborPosition);

            var updated = UpdateShape(level, state, position, face, neighbor);
            if (!updated.IsSameState(state))
                level.SetBlock(position, updated);

            var updatedNeighbor = UpdateShape(level, neighbor, neighborPosition, face.Opposite(), updated);
            if (!updatedNeighbor.IsSameState(neighbor))
                level.SetBlock(neighborPosition, updatedNeighbor);
        });
    }

    /// <summary>The state of <paramref name="state"/> after its neighbor in <paramref name="direction"/> became <paramref name="neighbor"/>.</summary>
    private static IBlock UpdateShape(IWorldGenLevel level, IBlock state, Vector position, BlockFace direction, IBlock neighbor)
    {
        switch (state.BlockClass())
        {
            case "FenceBlock":
                ScheduleWaterTick(level, state, position);
                return IsHorizontal(direction)
                    ? state.WithProperty(direction.PropertyName(), FenceConnectsTo(state, neighbor, direction.Opposite()))
                    : state;
            case "IronBarsBlock" or "StainedGlassPaneBlock" or "WeatheringCopperBarsBlock":
                ScheduleWaterTick(level, state, position);
                return IsHorizontal(direction)
                    ? state.WithProperty(direction.PropertyName(), BarsAttachTo(neighbor, neighbor.IsFaceSturdy(direction.Opposite())))
                    : state;
            case "TorchBlock":
                return direction == BlockFace.Down && !level.GetBlock(position + Vector.Down).IsTopCenterSturdy() ? air : state;
            case "WallTorchBlock":
                return IsUnsupportedWallBlock(level, state, position, direction) ? air : state;
            case "LadderBlock":
                if (IsUnsupportedWallBlock(level, state, position, direction))
                    return air;

                ScheduleWaterTick(level, state, position);
                return state;
            case "VineBlock":
                return direction == BlockFace.Down ? state : UpdateVine(level, state, position);
            case "DoublePlantBlock" or "TallFlowerBlock":
                return UpdateDoublePlant(level, state, position, direction, neighbor);
            case "HangingMossBlock":
                return state.WithProperty("tip", level.GetBlock(position + Vector.Down).RegistryId != state.RegistryId);
            case "LiquidBlock":
                // LiquidBlock.updateShape only schedules a fluid tick.
                if (state.IsFluidSource() || neighbor.IsFluidSource())
                    level.ScheduleFluidTick(position);
                return state;
        }

        // SnowyDirtBlock and its subclasses (grass blocks, mycelium, podzol).
        if (direction == BlockFace.Up && state.HasProperty("snowy"))
            return state.WithProperty("snowy", TreeBlocks.Snow.Contains(neighbor));

        return state;
    }

    /// <summary>Vanilla <c>VineBlock.getUpdatedState</c>; a vine left without faces becomes air.</summary>
    private static IBlock UpdateVine(IWorldGenLevel level, IBlock state, Vector position)
    {
        var abovePosition = position + Vector.Up;
        if (state.GetProperty("up") == "true")
            state = state.WithProperty("up", CanAttachTo(level.GetBlock(abovePosition), BlockFace.Down));

        IBlock? above = null;
        foreach (var face in TreeDirections.Horizontal)
        {
            var property = face.PropertyName();
            if (state.GetProperty(property) != "true")
                continue;

            var supported = CanAttachTo(level.GetBlock(position + face.ToVector()), face);
            if (!supported)
            {
                // Hanging from a vine above with the same face.
                above ??= level.GetBlock(abovePosition);
                supported = above.RegistryId == state.RegistryId && above.GetProperty(property) == "true";
            }

            state = state.WithProperty(property, supported);
        }

        var hasFace = state.GetProperty("up") == "true" || TreeDirections.Horizontal.Any(face => state.GetProperty(face.PropertyName()) == "true");
        return hasFace ? state : air;
    }

    /// <summary>
    /// Vanilla <c>MultifaceBlock.canAttachTo</c>: the neighbor's support or collision shape covers the face touching the
    /// vine. Approximated with the sturdy-face flag and full collision cubes from the block physics data.
    /// </summary>
    private static bool CanAttachTo(IBlock neighbor, BlockFace direction) =>
        neighbor.IsFaceSturdy(direction.Opposite()) || neighbor.IsCollisionShapeFullBlock();

    /// <summary>Vanilla <c>DoublePlantBlock.updateShape</c>: a half whose partner disappeared turns into air.</summary>
    private static IBlock UpdateDoublePlant(IWorldGenLevel level, IBlock state, Vector position, BlockFace direction, IBlock neighbor)
    {
        var isLower = state.GetProperty("half") == "lower";
        var vertical = direction is BlockFace.Up or BlockFace.Down;
        var partnerStillThere = neighbor.RegistryId == state.RegistryId && neighbor.GetProperty("half") != state.GetProperty("half");

        if (!vertical || isLower != (direction == BlockFace.Up) || partnerStillThere)
        {
            if (isLower && direction == BlockFace.Down)
            {
                // VegetationBlock.mayPlaceOn: dirt or farmland below.
                var below = level.GetBlock(position + Vector.Down);
                if (!TreeBlocks.Dirt.Contains(below) && !farmland.Contains(below))
                    return air;
            }

            return state;
        }

        return air;
    }

    private static bool IsHorizontal(BlockFace face) => face is not (BlockFace.Up or BlockFace.Down);

    private static void ScheduleWaterTick(IWorldGenLevel level, IBlock state, Vector position)
    {
        if (state.GetProperty("waterlogged") == "true")
            level.ScheduleFluidTick(position);
    }

    /// <summary>
    /// Vanilla <c>FenceBlock.connectsTo</c>: sturdy faces (except odd blocks), fences of the same kind (wooden or not) and
    /// fence gates turned across the connection.
    /// </summary>
    /// <param name="direction">The side of <paramref name="neighbor"/> facing the fence.</param>
    private static bool FenceConnectsTo(IBlock fence, IBlock neighbor, BlockFace direction)
    {
        var sameFence = fences.Contains(neighbor) && woodenFences.Contains(neighbor) == woodenFences.Contains(fence);
        var gate = neighbor.BlockClass() == "FenceGateBlock"
            && SameAxis(FeatureHelpers.ParseFace(neighbor.GetProperty("facing")), direction.ClockWise());

        return !IsExceptionForConnection(neighbor) && neighbor.IsFaceSturdy(direction) || sameFence || gate;
    }

    /// <summary>
    /// Vanilla <c>IronBarsBlock.attachsTo</c>: sturdy faces (except odd blocks), other bars and panes, and walls.
    /// </summary>
    private static bool BarsAttachTo(IBlock neighbor, bool sturdy) =>
        !IsExceptionForConnection(neighbor) && sturdy
        || neighbor.BlockClass() is "IronBarsBlock" or "StainedGlassPaneBlock" or "WeatheringCopperBarsBlock"
        || walls.Contains(neighbor);

    private static bool IsExceptionForConnection(IBlock block) => block.BlockClass().EndsWith("LeavesBlock") || connectionExceptions.Contains(block);

    /// <summary>
    /// Wall torches and ladders drop when the block behind them has no sturdy face, checked when that side is updated.
    /// </summary>
    private static bool IsUnsupportedWallBlock(IWorldGenLevel level, IBlock state, Vector position, BlockFace direction)
    {
        var facing = FeatureHelpers.ParseFace(state.GetProperty("facing"));
        return direction.Opposite() == facing && !level.GetBlock(position + facing.Opposite().ToVector()).IsFaceSturdy(facing);
    }

    private static bool SameAxis(BlockFace a, BlockFace b) => a == b || a == b.Opposite();
}
