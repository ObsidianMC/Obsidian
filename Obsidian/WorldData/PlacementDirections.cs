namespace Obsidian.WorldData;

/// <summary>
/// The directions a player placing a block faces and looks, like vanilla's <c>BlockPlaceContext</c> works them out
/// from the player's rotation.
/// </summary>
internal static class PlacementDirections
{
    // Vanilla Direction.from2DDataValue: south, west, north, east, a quarter turn of yaw apart from yaw 0.
    private static readonly BlockFace[] horizontal =
        [BlockFace.South, BlockFace.West, BlockFace.North, BlockFace.East];

    /// <summary>
    /// Vanilla <c>Direction.fromYRot</c>: the horizontal direction nearest <paramref name="yaw"/>, in degrees.
    /// </summary>
    public static BlockFace Horizontal(float yaw) => horizontal[(int)Math.Floor(yaw / 90.0 + 0.5) & 3];

    /// <summary>
    /// Vanilla <c>Direction.orderedByNearest(entity)[0]</c>: the direction, of all six, nearest where a player with
    /// the given rotation (in degrees) looks. It decides placed states players expect, so ties go as vanilla's do:
    /// north and south over east and west, and a horizontal direction over up and down.
    /// </summary>
    public static BlockFace Nearest(float yaw, float pitch)
    {
        var pitchRadians = pitch * (MathF.PI / 180);
        var yawRadians = -yaw * (MathF.PI / 180);

        var up = MathF.Sin(pitchRadians);
        var level = MathF.Cos(pitchRadians);
        var east = MathF.Sin(yawRadians);
        var south = MathF.Cos(yawRadians);

        var vertical = up < 0 ? BlockFace.Up : BlockFace.Down;
        var alongX = east > 0 ? BlockFace.East : BlockFace.West;
        var alongZ = south > 0 ? BlockFace.South : BlockFace.North;

        if (MathF.Abs(east) > MathF.Abs(south))
            return MathF.Abs(up) > MathF.Abs(east) * level ? vertical : alongX;

        return MathF.Abs(up) > MathF.Abs(south) * level ? vertical : alongZ;
    }
}
