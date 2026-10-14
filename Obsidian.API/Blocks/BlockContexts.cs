using Obsidian.API.Inventory;

namespace Obsidian.API.Blocks;

/// <summary>
/// A block a hook of <see cref="IBlockBehavior"/> runs for: where it is and its state.
/// </summary>
public class BlockContext
{
    /// <summary>
    /// The level, for reading and changing blocks during the hook.
    /// </summary>
    public required IBlockLevel Level { get; init; }

    public required Vector Position { get; init; }

    /// <summary>
    /// The block's state.
    /// </summary>
    public required IBlock State { get; init; }
}

/// <summary>
/// A block placed, or changed to another of its states (see <see cref="IBlockBehavior.OnPlaced"/>).
/// </summary>
public sealed class BlockPlacedContext : BlockContext
{
    /// <summary>
    /// The state there before.
    /// </summary>
    public required IBlock OldState { get; init; }
}

/// <summary>
/// A block another replaced (see <see cref="IBlockBehavior.OnRemoved"/>). <see cref="BlockContext.State"/> is the
/// removed block's state.
/// </summary>
public sealed class BlockRemovedContext : BlockContext
{
    /// <summary>
    /// The state that replaced it.
    /// </summary>
    public required IBlock NewState { get; init; }
}

/// <summary>
/// A block whose neighbour changed (see <see cref="IBlockBehavior.NeighborChanged"/>).
/// </summary>
public sealed class NeighborChangedContext : BlockContext
{
    /// <summary>
    /// Where the change was.
    /// </summary>
    public required Vector SourcePosition { get; init; }

    /// <summary>
    /// The block the change came from, like vanilla's: the state that was replaced when a neighbour changed, or the
    /// block that asked for the update.
    /// </summary>
    public required IBlock SourceBlock { get; init; }
}

/// <summary>
/// A block whose neighbour changed shape (see <see cref="IBlockBehavior.UpdateShape"/>).
/// </summary>
public sealed class ShapeUpdateContext : BlockContext
{
    /// <summary>
    /// The side of the block the neighbour is on.
    /// </summary>
    public required BlockFace Direction { get; init; }

    public required Vector NeighborPosition { get; init; }

    /// <summary>
    /// The neighbour's new state.
    /// </summary>
    public required IBlock NeighborState { get; init; }
}

/// <summary>
/// An entity touching a block (see <see cref="IBlockBehavior.EntityInsideAsync"/>).
/// </summary>
public sealed class EntityBlockContext
{
    public required IEntity Entity { get; init; }

    public required ILevel Level { get; init; }

    public required Vector Position { get; init; }

    /// <summary>
    /// The block's state.
    /// </summary>
    public required IBlock State { get; init; }
}

/// <summary>
/// A player placing a block (see <see cref="IBlockBehavior.GetStateForPlacement"/>), like vanilla's
/// <c>BlockPlaceContext</c>.
/// </summary>
public sealed class BlockPlaceContext
{
    /// <summary>
    /// The level, for reading the blocks around; the block isn't placed yet.
    /// </summary>
    public required IBlockLevel Level { get; init; }

    public required IPlayer Player { get; init; }

    public required InteractionHand Hand { get; init; }

    /// <summary>
    /// The item the player places the block with.
    /// </summary>
    public required ItemStack Item { get; init; }

    /// <summary>
    /// The block's default state.
    /// </summary>
    public required IBlock Block { get; init; }

    /// <summary>
    /// Where the block goes: <see cref="ClickedPosition"/> when it replaces the clicked block, otherwise next to it on
    /// the clicked face.
    /// </summary>
    public required Vector Position { get; init; }

    /// <summary>
    /// The block the player clicked.
    /// </summary>
    public required Vector ClickedPosition { get; init; }

    /// <summary>
    /// The face of the clicked block the player clicked.
    /// </summary>
    public required BlockFace ClickedFace { get; init; }

    /// <summary>
    /// Where on the clicked block the player clicked, from its lowest corner: 0 to 1 on each axis.
    /// </summary>
    public required VectorD ClickedAt { get; init; }

    /// <summary>
    /// Whether the block replaces the clicked block (tall grass, say) rather than going next to it.
    /// </summary>
    public required bool ReplacesClicked { get; init; }

    /// <summary>
    /// The horizontal direction the player faces (north, south, west or east), like vanilla's
    /// <c>getHorizontalDirection</c>. Blocks that face the player use its opposite.
    /// </summary>
    public required BlockFace HorizontalDirection { get; init; }

    /// <summary>
    /// The direction, of all six, closest to where the player looks, like vanilla's
    /// <c>getNearestLookingDirection</c>.
    /// </summary>
    public required BlockFace NearestLookDirection { get; init; }
}
