namespace Obsidian.WorldData.Placement;

/// <summary>
/// Placement rules shared by block kinds, so every placed block derives facing from the player's yaw the same way.
/// </summary>
internal static class PlacementFacing
{
    /// <summary>
    /// Vanilla <c>Direction.fromYRot</c> for horizontal directions: yaw 0 faces south, 90 faces west.
    /// </summary>
    public static BlockFace FromYaw(float yaw)
    {
        var index = ((int)MathF.Floor((yaw + 45) / 90) % 4 + 4) % 4;
        return index switch { 0 => BlockFace.South, 1 => BlockFace.West, 2 => BlockFace.North, _ => BlockFace.East };
    }
}
