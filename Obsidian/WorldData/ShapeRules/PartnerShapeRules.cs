using Obsidian.API.Blocks;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.ShapeRules;

// Shape rules of blocks made of two halves, which follow or lose their partner. Each is the rule of the vanilla class
// that declares updateShape, bound by name in ShapeUpdateBehaviors.

/// <summary>
/// Vanilla <c>DoorBlock.updateShape</c>: a half copies its partner's state, or becomes air without one; the lower half
/// needs a sturdy floor.
/// </summary>
internal sealed class DoorShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        var half = state.GetProperty("half");
        var isLower = half == "lower";
        if (direction is BlockFace.Up or BlockFace.Down && isLower == (direction == BlockFace.Up))
        {
            var partner = IsDoor(neighbor) && neighbor.GetProperty("half") != half;
            return partner ? neighbor.WithProperty("half", half!) : BlocksRegistry.Air;
        }

        var unsupported = isLower
            && direction == BlockFace.Down
            && !level.GetBlock(position + Vector.Down).IsFaceSturdy(BlockFace.Up);
        return unsupported ? BlocksRegistry.Air : state;
    }

    private static bool IsDoor(IBlock block) => block.BlockClass() is "DoorBlock" or "WeatheringCopperDoorBlock";
}

/// <summary>Vanilla <c>AbstractBedBlock.updateShape</c>: a bed half without its other half becomes air.</summary>
internal sealed class BedShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        var facing = FeatureHelpers.ParseFace(state.GetProperty("facing"));
        var part = state.GetProperty("part");
        if (direction != (part == "foot" ? facing : facing.Opposite()))
            return state;

        var partner = neighbor.RegistryId == state.RegistryId && neighbor.GetProperty("part") != part;
        return partner ? state.WithProperty("occupied", neighbor.GetProperty("occupied")!) : BlocksRegistry.Air;
    }
}

/// <summary>
/// Vanilla <c>ChestBlock.updateShape</c>: a single chest pairs with a half chest facing the same way that points at it,
/// and a half chest whose partner is gone becomes single.
/// </summary>
internal sealed class ChestShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        if (neighbor.RegistryId == state.RegistryId && IsHorizontal(direction))
        {
            var neighborType = neighbor.GetProperty("type");
            var pairs = state.GetProperty("type") == "single"
                && neighborType != "single"
                && state.GetProperty("facing") == neighbor.GetProperty("facing")
                && ConnectedDirection(neighbor) == direction.Opposite();
            if (pairs)
                return state.WithProperty("type", neighborType == "left" ? "right" : "left");
        }
        else if (ConnectedDirection(state) == direction)
        {
            return state.WithProperty("type", "single");
        }

        return state;
    }

    /// <summary>Vanilla <c>ChestBlock.getConnectedDirection</c>.</summary>
    private static BlockFace ConnectedDirection(IBlock chest)
    {
        var facing = FeatureHelpers.ParseFace(chest.GetProperty("facing"));
        return chest.GetProperty("type") == "left" ? facing.ClockWise() : facing.CounterClockWise();
    }
}

/// <summary>
/// Vanilla <c>DoublePlantBlock</c> and <c>SmallDripleafBlock</c> <c>updateShape</c>: a half whose partner disappeared
/// turns into air, and the lower half needs its ground.
/// </summary>
internal sealed class DoublePlantShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        var isLower = state.GetProperty("half") == "lower";
        var vertical = direction is BlockFace.Up or BlockFace.Down;
        var partnerStillThere = neighbor.RegistryId == state.RegistryId
            && neighbor.GetProperty("half") != state.GetProperty("half");

        if (vertical && isLower == (direction == BlockFace.Up) && !partnerStillThere)
            return BlocksRegistry.Air;

        if (isLower && direction == BlockFace.Down && !state.CanSurvive(level, position))
            return BlocksRegistry.Air;

        return state;
    }
}
