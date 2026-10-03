using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// Vanilla <c>Direction</c> helpers used by tree placement, with vanilla's iteration orders.
/// </summary>
internal static class TreeDirections
{
    /// <summary>Vanilla <c>Direction.Plane.HORIZONTAL</c> order: north, east, south, west.</summary>
    public static readonly BlockFace[] Horizontal = [BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West];

    /// <summary>Vanilla <c>Direction.values()</c> order (matches the <see cref="BlockFace"/> enum).</summary>
    public static readonly BlockFace[] All = [BlockFace.Down, BlockFace.Up, BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];

    /// <summary>Vanilla <c>Plane.HORIZONTAL.getRandomDirection(random)</c>.</summary>
    public static BlockFace RandomHorizontal(IRandomSource random) => Horizontal[random.NextInt(4)];

    public static BlockFace ClockWise(this BlockFace face) => face switch
    {
        BlockFace.North => BlockFace.East,
        BlockFace.East => BlockFace.South,
        BlockFace.South => BlockFace.West,
        BlockFace.West => BlockFace.North,
        _ => throw new ArgumentOutOfRangeException(nameof(face), "Only horizontal faces can be rotated.")
    };

    /// <summary>Whether the face points along a positive axis (up, south or east).</summary>
    public static bool IsPositive(this BlockFace face) => face is BlockFace.Up or BlockFace.South or BlockFace.East;

    /// <summary>The block state property name used by vines and similar blocks for the face (e.g. <c>east</c>).</summary>
    public static string PropertyName(this BlockFace face) => face switch
    {
        BlockFace.Down => "down",
        BlockFace.Up => "up",
        BlockFace.North => "north",
        BlockFace.South => "south",
        BlockFace.West => "west",
        _ => "east"
    };

    /// <summary>Vanilla <c>Vec3i.distManhattan</c>.</summary>
    public static int DistManhattan(Vector a, Vector b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);
}
