using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// Vanilla neighbour shape rules shared by live block updates, generation post-processing, trees and templates.
/// </summary>
/// <remarks>
/// These rules cover support loss, two-part partners, connections, stair shapes, snowy ground and fluid scheduling.
/// The live level supplies plugin and redstone reactions first, then applies these rules to vanilla blocks.
/// Other blocks keep their state; scheduled survival checks and other missing reactions are tracked in known gaps.
/// </remarks>
internal static class ShapeUpdater
{
    private static readonly IBlock air = BlockStateProperties.GetState("minecraft:air");

    private static readonly BlockSet fences = LegacyWorldgenTags.Blocks("fences");

    private static readonly BlockSet woodenFences = LegacyWorldgenTags.Blocks("wooden_fences");

    private static readonly BlockSet walls = LegacyWorldgenTags.Blocks("walls");

    private static readonly BlockSet wallPostOverride = LegacyWorldgenTags.Blocks("wall_post_override");

    private static readonly BlockSet wallHangingSigns = LegacyWorldgenTags.Blocks("wall_hanging_signs");

    // Blocks fences and bars never connect to for having a sturdy face (Block.isExceptionForConnection, besides leaves).
    private static readonly BlockSet connectionExceptions = new("minecraft:barrier", "minecraft:carved_pumpkin", "minecraft:jack_o_lantern",
        "minecraft:melon", "minecraft:pumpkin", $"#{LegacyWorldgenTags.Block("shulker_boxes").Identifier}");

    // Vanilla BlockBehaviour.UPDATE_SHAPE_ORDER.
    private static readonly BlockFace[] updateShapeOrder = [BlockFace.West, BlockFace.East, BlockFace.North,
        BlockFace.South, BlockFace.Down, BlockFace.Up];

    /// <summary>
    /// Vanilla <c>Block.updateFromNeighbourShapes</c>: <paramref name="state"/> updated against each of its neighbors.
    /// </summary>
    public static IBlock UpdateFromNeighbourShapes(IWorldGenLevel level, IBlock state, Vector position)
    {
        foreach (var direction in updateShapeOrder)
            state = UpdateShape(level, state, position, direction, level.GetBlock(position.Offset(direction)));

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

            var updated = UpdateShape(level, state, position, face, neighbor);
            if (!updated.IsSameState(state))
                level.SetBlock(position, updated);

            var updatedNeighbor = UpdateShape(level, neighbor, neighborPosition, face.Opposite(), updated);
            if (!updatedNeighbor.IsSameState(neighbor))
                level.SetBlock(neighborPosition, updatedNeighbor);
        });
    }

    /// <summary>
    /// Vanilla <c>updateShape</c>: the state of <paramref name="state"/> after its neighbor in <paramref name="direction"/>
    /// became <paramref name="neighbor"/>, scheduling the fluid tick vanilla schedules there.
    /// </summary>
    internal static IBlock UpdateShape(
        IWorldGenLevel level,
        IBlock state,
        Vector position,
        BlockFace direction,
        IBlock neighbor,
        int rawBrightness = 15,
        bool scheduleFluidTick = true)
    {
        if (scheduleFluidTick)
        {
            ScheduleFluidTick(level, state, position, neighbor);
        }

        switch (state.BlockClass())
        {
            case "MossyCarpetBlock":
                return Generators.Features.FeatureRegistry.UpdateMossCarpetShape(state, level, position);
            case "PointedDripstoneBlock" or "SulfurSpikeBlock":
                return Generators.Features.SpeleothemShapes.Update(level, state, position, direction);
            case "FenceBlock":
                return IsHorizontal(direction)
                    ? state.WithProperty(direction.PropertyName(), FenceConnectsTo(state, neighbor, direction.Opposite()))
                    : state;
            case "IronBarsBlock" or "StainedGlassPaneBlock" or "WeatheringCopperBarsBlock":
                return IsHorizontal(direction)
                    ? state.WithProperty(direction.PropertyName(), BarsAttachTo(neighbor, neighbor.IsFaceSturdy(direction.Opposite())))
                    : state;
            case "TorchBlock":
                return direction == BlockFace.Down && !level.GetBlock(position + Vector.Down).IsTopCenterSturdy() ? air : state;
            case "WallTorchBlock":
                return IsUnsupportedWallBlock(level, state, position, direction) ? air : state;
            case "LadderBlock":
                return IsUnsupportedWallBlock(level, state, position, direction) ? air : state;
            case "VineBlock":
                return direction == BlockFace.Down ? state : UpdateVine(level, state, position);
            case "DoublePlantBlock" or "TallFlowerBlock" or "TallSeagrassBlock" or "SmallDripleafBlock":
                return UpdateDoublePlant(level, state, position, direction, neighbor);
            case "HangingMossBlock":
                if (direction == BlockFace.Up && !state.CanSurvive(level, position))
                    return air;

                return state.WithProperty("tip", level.GetBlock(position + Vector.Down).RegistryId != state.RegistryId);
            case "StairBlock" or "WeatheringCopperStairBlock":
                return IsHorizontal(direction) ? state.WithProperty("shape", GetStairsShape(level, state, position)) : state;
            case "ChestBlock" or "TrappedChestBlock":
                return UpdateChest(state, direction, neighbor);
            case "WallBlock":
                return UpdateWall(level, state, position, direction, neighbor);
            case "DoorBlock" or "WeatheringCopperDoorBlock":
                return UpdateDoor(level, state, position, direction, neighbor);
            case "BedBlock":
                return UpdateBed(state, direction, neighbor);
            case "FenceGateBlock":
                return UpdateFenceGate(level, state, position, direction, neighbor);
            case "AttachedStemBlock":
                if (direction == FeatureHelpers.ParseFace(state.GetProperty("facing")) && !IsStemFruit(state, neighbor))
                    return BlockStateProperties.GetState(StemFor(state)).WithProperty("age", 7);

                return state.CanSurvive(level, position) ? state : air;
            case "WallBannerBlock" or "WallSignBlock":
                // These drop when the block behind them isn't solid.
                return direction == FeatureHelpers.ParseFace(state.GetProperty("facing")).Opposite()
                    && !level.GetBlock(position.Offset(direction)).IsSolid() ? air : state;
            case "BannerBlock" or "StandingSignBlock":
                return direction == BlockFace.Down && !level.GetBlock(position + Vector.Down).IsSolid() ? air : state;
            case "CeilingHangingSignBlock":
                return direction == BlockFace.Up
                    && !level.GetBlock(position + Vector.Up).IsFaceSturdy(BlockFace.Down, SupportType.Center)
                    ? air : state;
            case "WallHangingSignBlock":
                var sideways = FeatureHelpers.ParseFace(state.GetProperty("facing")).ClockWise();
                return SameAxis(direction, sideways) && !WallHangingSignSupported(level, state, position, sideways)
                    ? air : state;
            case "LeverBlock" or "ButtonBlock":
                return UpdateFaceAttached(level, state, position, direction);
            case "LanternBlock":
                return UpdateLantern(level, state, position, direction);
            case "RedstoneWireBlock":
                return UpdateRedstoneWire(level, state, position, direction, neighbor);
            case "RedstoneTorchBlock":
                return direction == BlockFace.Down && !level.GetBlock(position + Vector.Down).IsTopCenterSturdy() ? air : state;
            case "RedstoneWallTorchBlock":
                return IsUnsupportedWallBlock(level, state, position, direction) ? air : state;
            case "CarpetBlock" or "WoolCarpetBlock" or "FlowerBlock" or "TallGrassBlock"
                or "SweetBerryBushBlock" or "BushBlock" or "SaplingBlock" or "CropBlock"
                or "CarrotBlock" or "PotatoBlock" or "BeetrootBlock" or "StemBlock" or "MushroomBlock"
                or "LilyPadBlock" or "FlowerBedBlock" or "FireflyBushBlock"
                or "DryVegetationBlock" or "ShortDryGrassBlock"
                or "TallDryGrassBlock" or "LeafLitterBlock" or "VegetationBlock" or "EyeblossomBlock"
                or "WitherRoseBlock" or "AzaleaBlock" or "NetherFungusBlock" or "NetherRootsBlock" or "NetherSproutsBlock"
                or "SeagrassBlock" or "SnowLayerBlock" or "HangingRootsBlock" or "SporeBlossomBlock"
                or "CocoaBlock" or "CactusFlowerBlock":
                // Vegetation and carpets turn into air whenever they're updated without support.
                return state.CanSurvive(level, position, rawBrightness) ? state : air;
        }

        // SnowyDirtBlock and its subclasses (grass blocks, mycelium, podzol).
        if (direction == BlockFace.Up && state.HasProperty("snowy"))
            return state.WithProperty("snowy", TreeBlocks.Snow.Contains(neighbor));

        return state;
    }

    /// <summary>
    /// Vanilla <c>RedstoneWireBlock.updateShape</c> (see <see cref="WireConnections.Reconnect"/>), with vanilla's
    /// signal sources: generation has no plugin blocks to ask.
    /// </summary>
    private static IBlock UpdateRedstoneWire(
        IWorldGenLevel level,
        IBlock state,
        Vector position,
        BlockFace direction,
        IBlock neighbor) =>
        WireConnections.Reconnect(
            new WireView(level.GetBlock, block => block.IsSignalSource()),
            state,
            position,
            direction,
            neighbor);

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

            var supported = CanAttachTo(level.GetBlock(position.Offset(face)), face);
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
                if (!state.CanSurvive(level, position))
                    return air;
            }

            return state;
        }

        return air;
    }

    private static bool IsHorizontal(BlockFace face) => face is not (BlockFace.Up or BlockFace.Down);

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
    internal static bool BarsAttachTo(IBlock neighbor, bool sturdy) =>
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

    // Vanilla wall hanging signs need either end of their bar supported, or another aligned wall hanging sign.
    private static bool WallHangingSignSupported(
        IWorldGenLevel level, IBlock state, Vector position, BlockFace sideways)
    {
        foreach (var side in new[] { sideways, sideways.Opposite() })
        {
            var neighbor = level.GetBlock(position.Offset(side));
            if (wallHangingSigns.Contains(neighbor))
            {
                var facing = FeatureHelpers.ParseFace(state.GetProperty("facing"));
                var neighborFacing = FeatureHelpers.ParseFace(neighbor.GetProperty("facing"));
                if (SameAxis(facing, neighborFacing))
                    return true;
            }
            else if (neighbor.IsFaceSturdy(side.Opposite()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStairs(IBlock block) => block.BlockClass() is "StairBlock" or "WeatheringCopperStairBlock";

    /// <summary>Vanilla <c>StairBlock.getStairsShape</c>: corners from the stairs in front of and behind this one.</summary>
    private static string GetStairsShape(IWorldGenLevel level, IBlock state, Vector position)
    {
        var facing = FeatureHelpers.ParseFace(state.GetProperty("facing"));
        var half = state.GetProperty("half");

        var front = level.GetBlock(position.Offset(facing));
        if (IsStairs(front) && front.GetProperty("half") == half)
        {
            var frontFacing = FeatureHelpers.ParseFace(front.GetProperty("facing"));
            if (!SameAxis(frontFacing, facing) && CanTakeShape(level, state, position, frontFacing.Opposite()))
                return frontFacing == facing.CounterClockWise() ? "outer_left" : "outer_right";
        }

        var back = level.GetBlock(position.Offset(facing.Opposite()));
        if (IsStairs(back) && back.GetProperty("half") == half)
        {
            var backFacing = FeatureHelpers.ParseFace(back.GetProperty("facing"));
            if (!SameAxis(backFacing, facing) && CanTakeShape(level, state, position, backFacing))
                return backFacing == facing.CounterClockWise() ? "inner_left" : "inner_right";
        }

        return "straight";
    }

    private static bool CanTakeShape(IWorldGenLevel level, IBlock state, Vector position, BlockFace direction)
    {
        var neighbor = level.GetBlock(position.Offset(direction));
        return !IsStairs(neighbor) || neighbor.GetProperty("facing") != state.GetProperty("facing")
            || neighbor.GetProperty("half") != state.GetProperty("half");
    }

    /// <summary>
    /// Vanilla <c>ChestBlock.updateShape</c>: a single chest pairs with a half chest facing the same way that points at it,
    /// and a half chest whose partner is gone becomes single.
    /// </summary>
    private static IBlock UpdateChest(IBlock state, BlockFace direction, IBlock neighbor)
    {
        if (neighbor.RegistryId == state.RegistryId && IsHorizontal(direction))
        {
            var neighborType = neighbor.GetProperty("type");
            if (state.GetProperty("type") == "single" && neighborType != "single" && state.GetProperty("facing") == neighbor.GetProperty("facing")
                && ChestConnectedDirection(neighbor) == direction.Opposite())
            {
                return state.WithProperty("type", neighborType == "left" ? "right" : "left");
            }
        }
        else if (ChestConnectedDirection(state) == direction)
        {
            return state.WithProperty("type", "single");
        }

        return state;
    }

    /// <summary>Vanilla <c>ChestBlock.getConnectedDirection</c>.</summary>
    private static BlockFace ChestConnectedDirection(IBlock chest)
    {
        var facing = FeatureHelpers.ParseFace(chest.GetProperty("facing"));
        return chest.GetProperty("type") == "left" ? facing.ClockWise() : facing.CounterClockWise();
    }

    /// <summary>
    /// Vanilla <c>DoorBlock.updateShape</c>: a half copies its partner's state, or becomes air without one; the lower half
    /// needs a sturdy floor.
    /// </summary>
    private static IBlock UpdateDoor(IWorldGenLevel level, IBlock state, Vector position, BlockFace direction, IBlock neighbor)
    {
        var half = state.GetProperty("half");
        var isLower = half == "lower";
        if (direction is BlockFace.Up or BlockFace.Down && isLower == (direction == BlockFace.Up))
            return IsDoor(neighbor) && neighbor.GetProperty("half") != half ? neighbor.WithProperty("half", half!) : air;

        return isLower && direction == BlockFace.Down && !level.GetBlock(position + Vector.Down).IsFaceSturdy(BlockFace.Up) ? air : state;
    }

    private static bool IsDoor(IBlock block) => block.BlockClass() is "DoorBlock" or "WeatheringCopperDoorBlock";

    /// <summary>Vanilla <c>BedBlock.updateShape</c>: a bed half without its other half becomes air.</summary>
    private static IBlock UpdateBed(IBlock state, BlockFace direction, IBlock neighbor)
    {
        var facing = FeatureHelpers.ParseFace(state.GetProperty("facing"));
        var part = state.GetProperty("part");
        if (direction != (part == "foot" ? facing : facing.Opposite()))
            return state;

        return neighbor.RegistryId == state.RegistryId && neighbor.GetProperty("part") != part
            ? state.WithProperty("occupied", neighbor.GetProperty("occupied")!)
            : air;
    }

    /// <summary>Vanilla <c>FenceGateBlock.updateShape</c>: the gate lowers between walls.</summary>
    private static IBlock UpdateFenceGate(IWorldGenLevel level, IBlock state, Vector position, BlockFace direction, IBlock neighbor)
    {
        var facing = FeatureHelpers.ParseFace(state.GetProperty("facing"));
        if (!SameAxis(facing.ClockWise(), direction))
            return state;

        return state.WithProperty("in_wall", walls.Contains(neighbor) || walls.Contains(level.GetBlock(position.Offset(direction.Opposite()))));
    }

    private static bool IsStemFruit(IBlock stem, IBlock neighbor) =>
        neighbor.RegistryId == (stem.Is(BlockIds.AttachedMelonStem) ? BlockIds.Melon : BlockIds.Pumpkin);

    private static string StemFor(IBlock attached) =>
        attached.Is(BlockIds.AttachedMelonStem) ? "minecraft:melon_stem" : "minecraft:pumpkin_stem";

    /// <summary>
    /// Vanilla <c>FaceAttachedHorizontalDirectionalBlock.updateShape</c> (levers, buttons): drops without a sturdy face to
    /// hang on.
    /// </summary>
    private static IBlock UpdateFaceAttached(IWorldGenLevel level, IBlock state, Vector position, BlockFace direction)
    {
        var connected = state.GetProperty("face") switch
        {
            "ceiling" => BlockFace.Down,
            "floor" => BlockFace.Up,
            _ => FeatureHelpers.ParseFace(state.GetProperty("facing"))
        };

        if (connected.Opposite() != direction)
            return state;

        return level.GetBlock(position.Offset(direction)).IsFaceSturdy(connected) ? state : air;
    }

    /// <summary>Vanilla <c>LanternBlock.updateShape</c>: drops without a block to stand on or hang from.</summary>
    private static IBlock UpdateLantern(IWorldGenLevel level, IBlock state, Vector position, BlockFace direction)
    {
        var hanging = state.GetProperty("hanging") == "true";
        if (direction != (hanging ? BlockFace.Up : BlockFace.Down))
            return state;

        var support = level.GetBlock(position.Offset(direction));
        return (hanging ? support.IsBottomCenterSturdy() : support.IsTopCenterSturdy()) ? state : air;
    }

    /// <summary>
    /// Vanilla <c>WallBlock.updateShape</c>: connections to the side that changed, and side heights and the post from the
    /// bottom face of the block above.
    /// </summary>
    private static IBlock UpdateWall(IWorldGenLevel level, IBlock state, Vector position, BlockFace direction, IBlock neighbor)
    {
        if (direction == BlockFace.Down)
            return state;

        if (direction == BlockFace.Up)
        {
            return UpdateWallShape(state, neighbor, IsWallConnected(state, "north"), IsWallConnected(state, "east"),
                IsWallConnected(state, "south"), IsWallConnected(state, "west"));
        }

        var side = direction.Opposite();
        var connects = WallConnectsTo(neighbor, neighbor.IsFaceSturdy(side), side);
        return UpdateWallShape(state, level.GetBlock(position + Vector.Up),
            direction == BlockFace.North ? connects : IsWallConnected(state, "north"),
            direction == BlockFace.East ? connects : IsWallConnected(state, "east"),
            direction == BlockFace.South ? connects : IsWallConnected(state, "south"),
            direction == BlockFace.West ? connects : IsWallConnected(state, "west"));
    }

    private static bool IsWallConnected(IBlock wall, string side) => wall.GetProperty(side) != "none";

    /// <summary>
    /// Vanilla <c>WallBlock.connectsTo</c>: walls, sturdy faces (except odd blocks), bars and panes, and fence gates turned
    /// across the connection.
    /// </summary>
    private static bool WallConnectsTo(IBlock neighbor, bool sturdy, BlockFace direction)
    {
        var gate = neighbor.BlockClass() == "FenceGateBlock"
            && SameAxis(FeatureHelpers.ParseFace(neighbor.GetProperty("facing")), direction.ClockWise());
        return walls.Contains(neighbor) || !IsExceptionForConnection(neighbor) && sturdy
            || neighbor.BlockClass() is "IronBarsBlock" or "StainedGlassPaneBlock" or "WeatheringCopperBarsBlock" || gate;
    }

    private static IBlock UpdateWallShape(IBlock state, IBlock above, bool north, bool east, bool south, bool west)
    {
        var covers = WallShapeCovers.Get(above);
        state = state
            .WithProperty("north", WallSide(north, covers, WallShapeCovers.North))
            .WithProperty("east", WallSide(east, covers, WallShapeCovers.East))
            .WithProperty("south", WallSide(south, covers, WallShapeCovers.South))
            .WithProperty("west", WallSide(west, covers, WallShapeCovers.West));

        return state.WithProperty("up", ShouldRaisePost(state, above, covers));
    }

    private static string WallSide(bool connected, int covers, int side) => !connected ? "none" : (covers & side) != 0 ? "tall" : "low";

    /// <summary>Vanilla <c>WallBlock.shouldRaisePost</c>.</summary>
    private static bool ShouldRaisePost(IBlock state, IBlock above, int covers)
    {
        if (walls.Contains(above) && above.GetProperty("up") == "true")
            return true;

        var north = state.GetProperty("north");
        var south = state.GetProperty("south");
        var east = state.GetProperty("east");
        var west = state.GetProperty("west");
        var southNone = south == "none";
        var westNone = west == "none";
        var eastNone = east == "none";
        var northNone = north == "none";
        if (northNone && southNone && westNone && eastNone || northNone != southNone || westNone != eastNone)
            return true;

        if (north == "tall" && south == "tall" || east == "tall" && west == "tall")
            return false;

        return wallPostOverride.Contains(above) || (covers & WallShapeCovers.Post) != 0;
    }
}
