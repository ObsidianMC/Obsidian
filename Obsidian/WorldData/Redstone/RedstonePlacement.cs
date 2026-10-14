using Obsidian.API.Blocks;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// A player placing one of vanilla's redstone components, like vanilla's <c>BlockPlaceContext</c> (see
/// <see cref="RedstoneComponent.GetStateForPlacement"/>).
/// </summary>
internal sealed class RedstonePlacement
{
    /// <summary>
    /// The level, for reading the blocks around; the block isn't placed yet.
    /// </summary>
    public required LevelBlockUpdates Level { get; init; }

    /// <summary>
    /// The default state of the block the item places.
    /// </summary>
    public required IBlock Block { get; init; }

    /// <summary>
    /// Where the block goes.
    /// </summary>
    public required Vector Position { get; init; }

    /// <summary>
    /// Vanilla <c>getHorizontalDirection</c>: the horizontal direction the player faces.
    /// </summary>
    public required BlockFace HorizontalDirection { get; init; }

    /// <summary>
    /// Vanilla <c>getNearestLookingDirections</c> (see <see cref="PlacementDirections.NearestLooking"/>).
    /// </summary>
    public required IReadOnlyList<BlockFace> NearestLookingDirections { get; init; }
}
