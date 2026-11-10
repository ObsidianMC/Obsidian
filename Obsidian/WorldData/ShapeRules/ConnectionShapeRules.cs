using Obsidian.API.Blocks;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.Tree;
using Obsidian.WorldData.Redstone;
using TreeDirections = Obsidian.API.World.Features.Trees.TreeDirections;

namespace Obsidian.WorldData.ShapeRules;

// Shape rules of blocks whose state follows the blocks around them: connections, corners, faces and snow. Each is the
// rule of the vanilla class that declares updateShape, bound by name in ShapeUpdateBehaviors.

/// <summary>What fences, bars, panes and walls connect to, shared by their shape and placement rules.</summary>
internal static class ShapeConnections
{
    private static readonly WorldgenTags.BlockGroup walls = WorldgenTags.Blocks("walls");

    private static readonly WorldgenTags.BlockGroup shulkerBoxes = WorldgenTags.Blocks("shulker_boxes");

    // Blocks fences and bars never connect to for having a sturdy face (Block.isExceptionForConnection, besides
    // leaves).
    private static readonly BlockSet connectionExceptions = new("minecraft:barrier", "minecraft:carved_pumpkin",
        "minecraft:jack_o_lantern", "minecraft:melon", "minecraft:pumpkin");

    /// <summary>Whether <paramref name="block"/> is in vanilla's <c>walls</c> tag.</summary>
    public static bool IsWall(IBlock block) => walls.Contains(block);

    /// <summary>Whether <paramref name="block"/> is iron bars, a glass pane or copper bars.</summary>
    public static bool IsBars(IBlock block) =>
        block.BlockClass() is "IronBarsBlock" or "StainedGlassPaneBlock" or "WeatheringCopperBarsBlock";

    /// <summary>
    /// Vanilla <c>IronBarsBlock.attachsTo</c>: sturdy faces (except odd blocks), other bars and panes, and walls.
    /// </summary>
    public static bool BarsAttachTo(IBlock neighbor, bool sturdy) =>
        !IsExceptionForConnection(neighbor) && sturdy || IsBars(neighbor) || IsWall(neighbor);

    /// <summary>Vanilla <c>Block.isExceptionForConnection</c>: leaves, shulker boxes and a few odd blocks.</summary>
    public static bool IsExceptionForConnection(IBlock block) =>
        block.BlockClass().EndsWith("LeavesBlock")
        || connectionExceptions.Contains(block)
        || shulkerBoxes.Contains(block);
}

/// <summary>
/// Vanilla <c>WallBlock.updateShape</c>: connections to the side that changed, and side heights and the post from the
/// bottom face of the block above.
/// </summary>
internal sealed class WallShapeRule : SharedShapeRule
{
    private static readonly WorldgenTags.BlockGroup wallPostOverride = WorldgenTags.Blocks("wall_post_override");

    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        if (direction == BlockFace.Down)
            return state;

        if (direction == BlockFace.Up)
        {
            return UpdateWallShape(state, neighbor, IsConnected(state, "north"), IsConnected(state, "east"),
                IsConnected(state, "south"), IsConnected(state, "west"));
        }

        var side = direction.Opposite();
        var connects = ConnectsTo(neighbor, neighbor.IsFaceSturdy(side), side);
        return UpdateWallShape(state, level.GetBlock(position + Vector.Up),
            direction == BlockFace.North ? connects : IsConnected(state, "north"),
            direction == BlockFace.East ? connects : IsConnected(state, "east"),
            direction == BlockFace.South ? connects : IsConnected(state, "south"),
            direction == BlockFace.West ? connects : IsConnected(state, "west"));
    }

    private static bool IsConnected(IBlock wall, string side) => wall.GetProperty(side) != "none";

    /// <summary>
    /// Vanilla <c>WallBlock.connectsTo</c>: walls, sturdy faces (except odd blocks), bars and panes, and fence gates
    /// turned across the connection.
    /// </summary>
    private static bool ConnectsTo(IBlock neighbor, bool sturdy, BlockFace direction)
    {
        var gate = neighbor.BlockClass() == "FenceGateBlock"
            && SameAxis(FeatureHelpers.ParseFace(neighbor.GetProperty("facing")), direction.ClockWise());
        return ShapeConnections.IsWall(neighbor)
            || !ShapeConnections.IsExceptionForConnection(neighbor) && sturdy
            || ShapeConnections.IsBars(neighbor)
            || gate;
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

    private static string WallSide(bool connected, int covers, int side)
    {
        if (!connected)
            return "none";

        return (covers & side) != 0 ? "tall" : "low";
    }

    /// <summary>Vanilla <c>WallBlock.shouldRaisePost</c>.</summary>
    private static bool ShouldRaisePost(IBlock state, IBlock above, int covers)
    {
        if (ShapeConnections.IsWall(above) && above.GetProperty("up") == "true")
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

/// <summary>Vanilla <c>FenceGateBlock.updateShape</c>: the gate lowers between walls.</summary>
internal sealed class FenceGateShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        var facing = FeatureHelpers.ParseFace(state.GetProperty("facing"));
        if (!SameAxis(facing.ClockWise(), direction))
            return state;

        var inWall = ShapeConnections.IsWall(neighbor)
            || ShapeConnections.IsWall(level.GetBlock(position.Offset(direction.Opposite())));
        return state.WithProperty("in_wall", inWall);
    }
}

/// <summary>
/// Vanilla <c>StairBlock.updateShape</c>: a horizontal change re-decides the corner from the stairs in front of and
/// behind this one.
/// </summary>
internal sealed class StairShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor) =>
        IsHorizontal(direction) ? state.WithProperty("shape", GetStairsShape(level, state, position)) : state;

    private static bool IsStairs(IBlock block) => block.BlockClass() is "StairBlock" or "WeatheringCopperStairBlock";

    /// <summary>Vanilla <c>StairBlock.getStairsShape</c>.</summary>
    private static string GetStairsShape(IShapeLevel level, IBlock state, Vector position)
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

    private static bool CanTakeShape(IShapeLevel level, IBlock state, Vector position, BlockFace direction)
    {
        var neighbor = level.GetBlock(position.Offset(direction));
        return !IsStairs(neighbor)
            || neighbor.GetProperty("facing") != state.GetProperty("facing")
            || neighbor.GetProperty("half") != state.GetProperty("half");
    }
}

/// <summary>
/// Vanilla <c>VineBlock.updateShape</c> and <c>getUpdatedState</c>: each face keeps only what still holds it, and a
/// vine left without faces becomes air.
/// </summary>
internal sealed class VineShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        if (direction == BlockFace.Down)
            return state;

        var abovePosition = position + Vector.Up;
        if (state.GetProperty("up") == "true")
            state = state.WithProperty("up", CanAttachTo(level.GetBlock(abovePosition), BlockFace.Down));

        IBlock? above = null;
        foreach (var face in TreeDirections.Horizontal)
        {
            var property = TreeDirections.PropertyName(face);
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

        return HasFace(state) ? state : BlocksRegistry.Air;
    }

    private static bool HasFace(IBlock state)
    {
        if (state.GetProperty("up") == "true")
            return true;

        foreach (var face in TreeDirections.Horizontal)
        {
            if (state.GetProperty(TreeDirections.PropertyName(face)) == "true")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Vanilla <c>MultifaceBlock.canAttachTo</c>: the neighbor's support or collision shape covers the face touching
    /// the vine. Approximated with the sturdy-face flag and full collision cubes from the block physics data.
    /// </summary>
    private static bool CanAttachTo(IBlock neighbor, BlockFace direction) =>
        neighbor.IsFaceSturdy(direction.Opposite()) || neighbor.IsCollisionShapeFullBlock();
}

/// <summary>
/// Vanilla <c>RedstoneWireBlock.updateShape</c> (see <see cref="WireConnections.Reconnect"/>), with vanilla's signal
/// sources: the shared pass has no plugin blocks to ask.
/// </summary>
internal sealed class RedstoneWireShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor) =>
        WireConnections.Reconnect(
            new WireView(level.GetBlock, block => block.IsSignalSource()),
            state,
            position,
            direction,
            neighbor);
}

/// <summary>
/// Vanilla <c>SnowyBlock.updateShape</c> (grass blocks, mycelium, podzol): snowy while snow lies on top.
/// </summary>
internal sealed class SnowyShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor) =>
        direction == BlockFace.Up ? state.WithProperty("snowy", TreeBlocks.Snow.Contains(neighbor)) : state;
}
