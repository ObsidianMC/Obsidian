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
    /// the given rotation (in degrees) looks (see <see cref="OrderedByNearest"/>).
    /// </summary>
    public static BlockFace Nearest(float yaw, float pitch) => OrderedByNearest(yaw, pitch)[0];

    /// <summary>
    /// Vanilla <c>BlockPlaceContext.getNearestLookingDirections</c>: all six directions from nearest to farthest from
    /// where the player looks, with the clicked face's opposite moved first unless the block replaces the clicked one.
    /// Blocks that attach to a face (torches, levers, buttons) try them in this order.
    /// </summary>
    public static BlockFace[] NearestLooking(float yaw, float pitch, BlockFace clickedFace, bool replacesClicked)
    {
        var directions = OrderedByNearest(yaw, pitch);
        if (replacesClicked)
            return directions;

        var index = Array.IndexOf(directions, clickedFace.Opposite());
        if (index > 0)
        {
            Array.Copy(directions, 0, directions, 1, index);
            directions[0] = clickedFace.Opposite();
        }

        return directions;
    }

    /// <summary>
    /// Vanilla <c>Direction.orderedByNearest(entity)</c>: the six directions from nearest to farthest from where a
    /// player with the given rotation (in degrees) looks. The nearest decides placed states players expect, so ties
    /// go as vanilla's do: north and south over east and west, and a horizontal direction over up and down.
    /// </summary>
    public static BlockFace[] OrderedByNearest(float yaw, float pitch)
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

        var x = MathF.Abs(east);
        var y = MathF.Abs(up);
        var z = MathF.Abs(south);
        var levelX = x * level;
        var levelZ = z * level;

        if (x > z)
        {
            if (y > levelX)
                return Ordered(vertical, alongX, alongZ);

            return levelZ > y ? Ordered(alongX, alongZ, vertical) : Ordered(alongX, vertical, alongZ);
        }

        if (y > levelZ)
            return Ordered(vertical, alongZ, alongX);

        return levelX > y ? Ordered(alongZ, alongX, vertical) : Ordered(alongZ, vertical, alongX);
    }

    // Vanilla's makeDirectionArray: the three nearest, then their opposites from the farthest back.
    private static BlockFace[] Ordered(BlockFace first, BlockFace second, BlockFace third) =>
        [first, second, third, third.Opposite(), second.Opposite(), first.Opposite()];
}
