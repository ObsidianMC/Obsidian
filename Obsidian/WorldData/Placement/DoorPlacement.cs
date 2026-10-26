using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Placement;

/// <summary>
/// The two halves of a door placed by a player (vanilla <c>DoorBlock.getStateForPlacement</c> and <c>DoorItem.useOn</c>).
/// </summary>
internal sealed record DoorPlan(Vector LowerPosition, IBlock Lower, Vector UpperPosition, IBlock Upper)
{
    /// <summary>
    /// The plan with both halves opened and powered, or left closed. Vanilla <c>DoorBlock.getStateForPlacement</c> sets both from a
    /// neighbor signal at either half.
    /// </summary>
    public DoorPlan WithSignal(bool powered) => this with
    {
        Lower = Lower.WithProperty("open", powered ? "true" : "false").WithProperty("powered", powered ? "true" : "false"),
        Upper = Upper.WithProperty("open", powered ? "true" : "false").WithProperty("powered", powered ? "true" : "false"),
    };
}

internal static class DoorPlacement
{
    public static bool IsDoor(IBlock block) => block.BlockClass() is "DoorBlock" or "WeatheringCopperDoorBlock";

    /// <summary>
    /// Plans a player's door placement at <paramref name="position"/>, or returns null when vanilla refuses it: the door can't go on
    /// a bottom face, the space above must be free, and the block below must support it.
    /// </summary>
    public static async ValueTask<DoorPlan?> PlanAsync(IBlock door, Vector position, BlockFace clickedFace, float yaw, Func<Vector, ValueTask<IBlock?>> read)
    {
        if (clickedFace == BlockFace.Down)
            return null;

        var upperPosition = position + Vector.Up;
        var above = await read(upperPosition);
        var below = await read(position + Vector.Down);
        if (above is null || !above.IsAir || below is null || !below.IsFaceSturdy(BlockFace.Up))
            return null;

        var facing = PlacementFacing.FromYaw(yaw);
        var hinge = ChooseHinge(await SolidCount(read, position, facing.CounterClockWise()), await SolidCount(read, position, facing.ClockWise()));

        var lower = door.WithProperty("facing", facing.ToString().ToLowerInvariant())
            .WithProperty("half", "lower").WithProperty("hinge", hinge)
            .WithProperty("open", "false").WithProperty("powered", "false");
        return new DoorPlan(position, lower, upperPosition, lower.WithProperty("half", "upper"));
    }

    // Vanilla: the hinge goes on the side with more solid neighbours (left when they're tied).
    private static string ChooseHinge(int leftSolid, int rightSolid) =>
        rightSolid > leftSolid ? "right" : "left";

    private static async ValueTask<int> SolidCount(Func<Vector, ValueTask<IBlock?>> read, Vector position, BlockFace side)
    {
        var count = 0;
        foreach (var height in new[] { 0, 1 })
        {
            var neighbour = await read(position.Offset(side) + new Vector(0, height, 0));
            if (neighbour is not null && neighbour.IsFaceSturdy(side.Opposite()))
                count++;
        }

        return count;
    }
}
