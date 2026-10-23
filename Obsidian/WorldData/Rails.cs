namespace Obsidian.WorldData;

/// <summary>
/// Reading and writing rails' shapes (<see cref="RailShape"/>, in vanilla's order), and where they lead, like
/// vanilla's <c>BaseRailBlock</c> and <c>AbstractMinecart.exits</c>. Powered, detector and activator rails only run
/// straight.
/// </summary>
internal static class Rails
{
    private static readonly string[] names =
    [
        "north_south", "east_west", "ascending_east", "ascending_west", "ascending_north", "ascending_south",
        "south_east", "south_west", "north_west", "north_east"
    ];

    // Vanilla's AbstractMinecart.EXITS: the two neighbours a rail leads to, one block down for a slope's low end.
    private static readonly (Vector First, Vector Second)[] exits =
    [
        (new(0, 0, -1), new(0, 0, 1)),
        (new(-1, 0, 0), new(1, 0, 0)),
        (new(-1, -1, 0), new(1, 0, 0)),
        (new(-1, 0, 0), new(1, -1, 0)),
        (new(0, 0, -1), new(0, -1, 1)),
        (new(0, -1, -1), new(0, 0, 1)),
        (new(0, 0, 1), new(1, 0, 0)),
        (new(0, 0, 1), new(-1, 0, 0)),
        (new(0, 0, -1), new(-1, 0, 0)),
        (new(0, 0, -1), new(1, 0, 0))
    ];

    /// <summary>
    /// Vanilla's <c>BaseRailBlock.isRail</c>: a block in the <c>rails</c> tag.
    /// </summary>
    public static bool IsRail(IBlock? block) =>
        block is not null && TagsRegistry.Block.Rails.Entries.Contains(block.RegistryId);

    /// <summary>
    /// Vanilla's <c>isStraight</c>: every rail but the plain one, which also curves.
    /// </summary>
    public static bool IsStraight(IBlock block) => !block.Is(BlockIds.Rail);

    /// <summary>The shape a rail's state has.</summary>
    public static RailShape ShapeOf(IBlock rail)
    {
        var index = Array.IndexOf(names, rail.GetProperty("shape"));
        return index < 0 ? RailShape.NorthSouth : (RailShape)index;
    }

    /// <summary>The rail's state with another shape.</summary>
    public static IBlock WithShape(IBlock rail, RailShape shape) => rail.WithProperty("shape", names[(int)shape]);

    /// <summary>Whether a shape runs up a slope.</summary>
    public static bool IsSlope(RailShape shape) => shape is RailShape.AscendingEast or RailShape.AscendingWest
        or RailShape.AscendingNorth or RailShape.AscendingSouth;

    /// <summary>
    /// Vanilla's <c>AbstractMinecart.exits</c>: the offsets of the two neighbours the shape leads to.
    /// </summary>
    public static (Vector First, Vector Second) Exits(RailShape shape) => exits[(int)shape];

    /// <summary>
    /// Whether the block at <paramref name="position"/> is a powered or activator rail that's powered.
    /// </summary>
    public static bool IsPowered(IBlock block) => block.GetProperty("powered") == "true";
}
