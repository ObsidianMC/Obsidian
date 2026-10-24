using Obsidian.WorldData.Features;


namespace Obsidian.WorldData.Placement;

/// <summary>
/// A fence gate's placed state (vanilla <c>FenceGateBlock.getStateForPlacement</c>, checked against the 1.21.11 bytecode): it faces the
/// player, is open and powered when it has a neighbor signal, and is <c>in_wall</c> when a wall is on either side.
/// </summary>
internal static class FenceGatePlacement
{
    private static readonly BlockSet walls = new("#minecraft:walls");

    public static bool IsFenceGate(IBlock block) => block.BlockClass() == "FenceGateBlock";

    public static async ValueTask<IBlock> DeriveAsync(IBlock gate, Vector position, float yaw, bool hasNeighborSignal, Func<Vector, ValueTask<IBlock?>> read)
    {
        if (!IsFenceGate(gate))
            return gate;

        var facing = PlacementFacing.FromYaw(yaw);
        var inWall = false;
        foreach (var side in new[] { FeatureHelpers.ClockWise(facing), FeatureHelpers.CounterClockWise(facing) })
        {
            var neighbour = await read(position.Offset(side));
            inWall |= neighbour is not null && walls.Contains(neighbour);
        }

        var open = hasNeighborSignal ? "true" : "false";
        return gate.WithProperty("facing", facing.ToString().ToLowerInvariant())
            .WithProperty("open", open).WithProperty("powered", open).WithProperty("in_wall", inWall ? "true" : "false");
    }
}
