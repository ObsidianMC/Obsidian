using Obsidian.API.Blocks;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators;
using static Obsidian.WorldData.Placement.PlaceContexts;

namespace Obsidian.WorldData.Placement;

internal static partial class BlockPlacements
{
    private static readonly WorldgenTags.BlockGroup allHangingSigns = WorldgenTags.Blocks("all_hanging_signs");
    private static readonly WorldgenTags.BlockGroup wallHangingSigns = WorldgenTags.Blocks("wall_hanging_signs");

    /// <summary>
    /// Vanilla's wall blocks that try each side the player looks toward, nearest first, facing away from the first
    /// that holds them: wall torches, wall signs and banners, tripwire hooks, shelf mushrooms and coral fans.
    /// </summary>
    public static IBlock? OnWall(BlockPlaceContext context) => OnWall(context, context.Block);

    private static IBlock? OnWall(BlockPlaceContext context, IBlock state)
    {
        foreach (var direction in context.NearestLookingDirections())
        {
            if (direction.IsVertical())
                continue;

            state = state.WithProperty("facing", Name(direction.Opposite()));
            if (PlacementSurvival.CanSurvive(context.Level, state, context.Position))
                return state;
        }

        return null;
    }

    /// <summary>Vanilla <c>WallSignBlock</c>: on the nearest wall that's solid, taking the water there.</summary>
    public static IBlock? WallSign(BlockPlaceContext context) => OnWall(context)?.WaterloggedFor(context);

    /// <summary>Vanilla <c>BaseCoralWallFanBlock</c>: a coral fan on the nearest wall that holds it.</summary>
    public static IBlock? CoralWallFan(BlockPlaceContext context) => OnWall(context, FullWaterlogged(context));

    /// <summary>Vanilla <c>TripWireHookBlock</c>: unpowered and unattached, on the nearest wall that holds it.</summary>
    public static IBlock? TripWireHook(BlockPlaceContext context) =>
        OnWall(context, context.Block.WithProperty("powered", false).WithProperty("attached", false));

    /// <summary>
    /// Vanilla <c>LadderBlock</c>: like a wall torch, but a ladder clicked on the face of a ladder it would lie flat
    /// against isn't placed.
    /// </summary>
    public static IBlock? Ladder(BlockPlaceContext context)
    {
        if (!context.ReplacesClicked)
        {
            var clicked = context.Level.GetBlock(context.Position.Offset(context.ClickedFace.Opposite()));
            var flatAgainst = clicked.GetProperty("facing") == Name(context.ClickedFace);
            if (clicked.RegistryId == context.Block.RegistryId && flatAgainst)
                return null;
        }

        return OnWall(context)?.WaterloggedFor(context);
    }

    /// <summary>Vanilla <c>RedstoneWallTorchBlock</c>: wherever a wall torch would go.</summary>
    public static IBlock? RedstoneWallTorch(BlockPlaceContext context)
    {
        var torch = OnWall(context, BlocksRegistry.WallTorch);
        return torch is null ? null : context.Block.WithProperty("facing", torch.GetProperty("facing")!);
    }

    /// <summary>Vanilla <c>CocoaBlock</c>: facing the nearest jungle log the player looks toward.</summary>
    public static IBlock? Cocoa(BlockPlaceContext context)
    {
        foreach (var direction in context.NearestLookingDirections())
        {
            if (direction.IsVertical())
                continue;

            var state = context.Block.WithProperty("facing", Name(direction));
            if (PlacementSurvival.CanSurvive(context.Level, state, context.Position))
                return state;
        }

        return null;
    }

    /// <summary>
    /// Vanilla <c>WallSkullBlock</c>: on the nearest side the player looks toward where there's a block it can't
    /// replace.
    /// </summary>
    public static IBlock? WallSkull(BlockPlaceContext context)
    {
        var state = Skull(context);
        foreach (var direction in context.NearestLookingDirections())
        {
            if (direction.IsVertical())
                continue;

            state = state.WithProperty("facing", Name(direction.Opposite()));
            if (!context.Level.GetBlock(context.Position.Offset(direction)).CanBeReplaced())
                return state;
        }

        return null;
    }

    /// <summary>Vanilla <c>AbstractSkullBlock</c>: powered by a signal where it's placed.</summary>
    public static IBlock Skull(BlockPlaceContext context) =>
        context.Block.WithProperty("powered", context.Level.HasNeighborSignal(context.Position));

    /// <summary>Vanilla <c>SkullBlock</c>: turned the way the player looks.</summary>
    public static IBlock StandingSkull(BlockPlaceContext context) =>
        Skull(context).WithProperty("rotation", RotationSegment(context.Rotation()));

    /// <summary>Vanilla <c>BannerBlock</c>: turned to face the player.</summary>
    public static IBlock Banner(BlockPlaceContext context) =>
        context.Block.WithProperty("rotation", RotationSegment(context.Rotation() + 180));

    /// <summary>Vanilla <c>StandingSignBlock</c>: turned to face the player.</summary>
    public static IBlock StandingSign(BlockPlaceContext context) => Banner(context).WaterloggedFor(context);

    /// <summary>
    /// Vanilla <c>CeilingHangingSignBlock</c>: under a full face it hangs straight, facing the player; under anything
    /// else, or placed sneaking, it hangs from chains and turns as freely as a standing sign. Under another hanging sign
    /// turned the same way, it hangs straight too.
    /// </summary>
    public static IBlock CeilingHangingSign(BlockPlaceContext context)
    {
        var above = context.Level.GetBlock(context.Position + Vector.Up);
        var direction = PlacementDirections.Horizontal(context.Rotation());
        var sneaking = context.Sneaking();
        var attachedToMiddle = !above.IsFaceSturdy(BlockFace.Down, SupportType.Full) || sneaking;
        if (allHangingSigns.Contains(above) && !sneaking && HangsAlong(above, direction))
            attachedToMiddle = false;

        var rotation = attachedToMiddle
            ? RotationSegment(context.Rotation() + 180)
            : RotationSegment(direction.Opposite());
        return context.Block
            .WithProperty("attached", attachedToMiddle)
            .WithProperty("rotation", rotation)
            .WaterloggedFor(context);
    }

    // Whether a hanging sign above lies along the same axis as the direction the player faces.
    private static bool HangsAlong(IBlock sign, BlockFace direction)
    {
        if (sign.GetProperty("facing") is string facing)
            return SameAxis(FeatureHelpers.ParseFace(facing), direction);

        if (sign.GetProperty("rotation") is not string rotation)
            return false;

        // Vanilla RotationSegment.convertToDirection: only the four square rotations name a direction.
        var segment = int.Parse(rotation);
        if (segment % 4 != 0)
            return false;

        BlockFace[] horizontal = [BlockFace.South, BlockFace.West, BlockFace.North, BlockFace.East];
        return SameAxis(horizontal[segment / 4], direction);
    }

    /// <summary>
    /// Vanilla <c>WallHangingSignBlock</c>: across the nearest horizontal direction the player looks toward that isn't
    /// along the clicked face, held from either side by a block or another wall hanging sign.
    /// </summary>
    public static IBlock? WallHangingSign(BlockPlaceContext context)
    {
        foreach (var direction in context.NearestLookingDirections())
        {
            if (direction.IsVertical() || SameAxis(direction, context.ClickedFace))
                continue;

            var state = context.Block.WithProperty("facing", Name(direction.Opposite()));
            if (WallHangingSignHolds(context.Level, state, context.Position))
                return state.WaterloggedFor(context);
        }

        return null;
    }

    /// <summary>
    /// Vanilla <c>WallHangingSignBlock.canPlace</c>: a block with a full face toward the sign, or a wall hanging sign
    /// along the same line, on one side of it.
    /// </summary>
    public static bool WallHangingSignHolds(IBlockLevel level, IBlock state, Vector position)
    {
        var facing = FeatureHelpers.ParseFace(state.GetProperty("facing"));
        foreach (var side in (BlockFace[])[facing.ClockWise(), facing.CounterClockWise()])
        {
            var neighbor = level.GetBlock(position.Offset(side));
            var holds = wallHangingSigns.Contains(neighbor)
                ? SameAxis(FeatureHelpers.ParseFace(neighbor.GetProperty("facing")), facing)
                : neighbor.IsFaceSturdy(side.Opposite(), SupportType.Full);
            if (holds)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Vanilla <c>FaceAttachedHorizontalDirectionalBlock</c> (grindstones; levers and buttons are redstone): on the first
    /// of the nearest looking directions it survives on. On the floor or ceiling it faces the way the player does; on a
    /// wall, away from it.
    /// </summary>
    public static IBlock? FaceAttached(BlockPlaceContext context)
    {
        foreach (var direction in context.NearestLookingDirections())
        {
            var state = direction.IsVertical()
                ? context.Block
                    .WithProperty("face", direction == BlockFace.Up ? "ceiling" : "floor")
                    .WithProperty("facing", Name(context.HorizontalDirection))
                : context.Block
                    .WithProperty("face", "wall")
                    .WithProperty("facing", Name(direction.Opposite()));

            if (PlacementSurvival.CanSurvive(context.Level, state, context.Position))
                return state;
        }

        return null;
    }

    /// <summary>
    /// Vanilla <c>LanternBlock</c>: hanging or standing, whichever of up and down the player looks toward first that
    /// holds it.
    /// </summary>
    public static IBlock? Lantern(BlockPlaceContext context)
    {
        foreach (var direction in context.NearestLookingDirections())
        {
            if (!direction.IsVertical())
                continue;

            var state = context.Block.WithProperty("hanging", direction == BlockFace.Up);
            if (PlacementSurvival.CanSurvive(context.Level, state, context.Position))
                return state.WaterloggedFor(context);
        }

        return null;
    }

    /// <summary>
    /// Vanilla <c>BellBlock</c>: clicked from above or below, on the floor or ceiling, facing the way the player does;
    /// clicked on a side, on that wall (or between two), or else on the floor or ceiling there.
    /// </summary>
    public static IBlock? Bell(BlockPlaceContext context)
    {
        var face = context.ClickedFace;
        var level = context.Level;
        var position = context.Position;
        if (face.IsVertical())
        {
            var hanging = context.Block
                .WithProperty("attachment", face == BlockFace.Down ? "ceiling" : "floor")
                .WithProperty("facing", Name(context.HorizontalDirection));
            return PlacementSurvival.CanSurvive(level, hanging, position) ? hanging : null;
        }

        var across = face.ClockWise();
        var doubleAttached = PlacementSurvival.CanAttach(level, position, across)
            && PlacementSurvival.CanAttach(level, position, across.Opposite());
        var state = context.Block
            .WithProperty("facing", Name(face.Opposite()))
            .WithProperty("attachment", doubleAttached ? "double_wall" : "single_wall");
        if (PlacementSurvival.CanSurvive(level, state, position))
            return state;

        var onFloor = level.GetBlock(position + Vector.Down).IsFaceSturdy(BlockFace.Up);
        state = state.WithProperty("attachment", onFloor ? "floor" : "ceiling");
        return PlacementSurvival.CanSurvive(level, state, position) ? state : null;
    }

    /// <summary>
    /// Vanilla <c>VineBlock</c>: on the nearest face, besides the bottom, the player looks toward that holds it, or onto
    /// a free face of the vine clicked.
    /// </summary>
    public static IBlock? Vine(BlockPlaceContext context)
    {
        var replaced = context.Replaced();
        var clickedVine = replaced.RegistryId == context.Block.RegistryId;
        var result = clickedVine ? replaced : context.Block;
        foreach (var direction in context.NearestLookingDirections())
        {
            if (direction == BlockFace.Down)
                continue;

            var occupied = clickedVine && replaced.GetProperty(Name(direction)) == "true";
            if (!occupied && VineHoldsAt(context, direction))
                return result.WithProperty(Name(direction), true);
        }

        return clickedVine ? result : null;
    }

    // Vanilla VineBlock.canSupportAtFace: a full face there, or for a side, the same side of a vine above.
    private static bool VineHoldsAt(BlockPlaceContext context, BlockFace direction)
    {
        var neighbor = context.Level.GetBlock(context.Position.Offset(direction));
        if (BlockSurvival.CanAttachTo(neighbor, direction))
            return true;

        if (direction.IsVertical())
            return false;

        var above = context.Level.GetBlock(context.Position + Vector.Up);
        return above.RegistryId == context.Block.RegistryId && above.GetProperty(Name(direction)) == "true";
    }

    /// <summary>
    /// Vanilla <c>MultifaceBlock</c> (glow lichen, sculk veins, resin): on the nearest face the player looks toward
    /// that it can cover and doesn't already.
    /// </summary>
    public static IBlock? Multiface(BlockPlaceContext context)
    {
        var replaced = context.Replaced();
        var same = replaced.RegistryId == context.Block.RegistryId;
        foreach (var direction in context.NearestLookingDirections())
        {
            if (same && replaced.GetProperty(Name(direction)) == "true")
                continue;

            var neighbor = context.Level.GetBlock(context.Position.Offset(direction));
            if (!BlockSurvival.CanAttachTo(neighbor, direction))
                continue;

            var state = context.Block;
            if (same)
                state = replaced;
            else if (replaced.GetFluid() == FluidKind.Water)
                state = state.WithProperty("waterlogged", true);

            return state.WithProperty(Name(direction), true);
        }

        return null;
    }

    /// <summary>Vanilla <c>AmethystClusterBlock</c>: growing out of the clicked face.</summary>
    public static IBlock AmethystCluster(BlockPlaceContext context) =>
        context.Block.WaterloggedFor(context).WithProperty("facing", Name(context.ClickedFace));
}
