namespace Obsidian.WorldData.Placement;

/// <summary>
/// A trapdoor's facing and half from the click (vanilla <c>TrapDoorBlock.getStateForPlacement</c>, checked against the 1.21.11 bytecode).
/// </summary>
internal static class TrapdoorPlacement
{
    public static bool IsTrapdoor(IBlock block) => block.BlockClass() == "TrapDoorBlock";

    /// <summary>
    /// A horizontal click faces the trapdoor toward the clicked face, with the top half when the click is in the upper half of the block.
    /// A click on the top or bottom face faces it away from the player, and the half follows the face (bottom for a top click).
    /// </summary>
    /// <param name="clickedY">The click's height inside the block, 0 to 1.</param>
    public static IBlock Derive(IBlock block, BlockFace clickedFace, double clickedY, float yaw)
    {
        if (!IsTrapdoor(block))
            return block;

        if (clickedFace is BlockFace.North or BlockFace.South or BlockFace.West or BlockFace.East)
        {
            var top = clickedY > 0.5;
            return block.WithProperty("facing", clickedFace.ToString().ToLowerInvariant())
                .WithProperty("half", top ? "top" : "bottom");
        }

        var facing = PlacementFacing.FromYaw(yaw).Opposite();
        return block.WithProperty("facing", facing.ToString().ToLowerInvariant())
            .WithProperty("half", clickedFace == BlockFace.Up ? "bottom" : "top");
    }
}
