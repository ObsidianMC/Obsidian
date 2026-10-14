using Obsidian.API.Inventory;

namespace Obsidian.API.Blocks;

/// <summary>
/// What a plugin's block does: when a player uses or places it, when it or a neighbour changes, when it ticks and when
/// entities touch it, and the redstone power it gives. Implement the methods the block needs; the others leave the
/// block to Obsidian's default handling, which does nothing.
/// </summary>
/// <remarks>
/// <para>
/// The behaviour belongs to the plugin and is dropped when the plugin unloads, while the block stays registered (it
/// stays in the world and keeps its states' ids), so keep state in the block's state or the plugin, not here. Every
/// method runs as a call into the plugin, and none runs once the plugin is unloading.
/// </para>
/// <para>
/// The hooks given an <see cref="IBlockLevel"/> (placement, changes, updates and ticks) run synchronously while the
/// level's block updates wait for them, like vanilla's: keep them short, and change blocks through that level, not
/// through <see cref="ILevel"/>. Updates they cause run after them, in vanilla's order.
/// </para>
/// </remarks>
public interface IBlockBehavior
{
    /// <summary>
    /// Called when a player uses the block (right-clicks it without sneaking), before the item in their hand is used.
    /// </summary>
    /// <returns>Whether the behaviour handled the use, which skips using the item.</returns>
    public ValueTask<bool> UseAsync(BlockUseContext context) => ValueTask.FromResult(false);

    /// <summary>
    /// Runs before a player breaks this block. Set IsCancelled to keep it, for example after removing one part.
    /// The hit comes from the server's outline raycast, since vanilla's break packet carries no hit location.
    /// Runs on the level tick, in turn with block entity callbacks and the resulting break transaction.
    /// </summary>
    public ValueTask BreakAsync(BlockBreakContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// The state a player places the block in, like vanilla's <c>getStateForPlacement</c>: from where they clicked
    /// and where they look, say a <c>facing</c> property from <see cref="BlockPlaceContext.HorizontalDirection"/>.
    /// </summary>
    /// <returns>The state to place, or null to not place the block (the player keeps the item).</returns>
    public IBlock? GetStateForPlacement(BlockPlaceContext context) => context.Block;

    /// <summary>
    /// Called when the block is placed or changes state, like vanilla's <c>onPlace</c>, unless the change skips it
    /// (<see cref="BlockUpdateFlags.SkipOnPlace"/>). <see cref="BlockPlacedContext.OldState"/> tells which: another
    /// block, or another state of this one.
    /// </summary>
    public void OnPlaced(BlockPlacedContext context)
    {
    }

    /// <summary>
    /// Called when another block replaces the block with neighbour updates (<see cref="BlockUpdateFlags.Neighbors"/>),
    /// like vanilla's <c>affectNeighborsAfterRemoval</c>; the new block is already there.
    /// </summary>
    public void OnRemoved(BlockRemovedContext context)
    {
    }

    /// <summary>
    /// Called when a neighbour changed with neighbour updates, or something else updated the block's neighbours, like
    /// vanilla's <c>neighborChanged</c>: the place to react to power or to a block it needs disappearing.
    /// </summary>
    public void NeighborChanged(NeighborChangedContext context)
    {
    }

    /// <summary>
    /// The block's state after its neighbour in <see cref="ShapeUpdateContext.Direction"/> changed, like vanilla's
    /// <c>updateShape</c>: a fence connecting to it, or a torch losing its support. Air destroys the block, leaving
    /// the fluid it held; it doesn't drop anything.
    /// </summary>
    /// <returns>The new state, or <see cref="BlockContext.State"/> to keep it.</returns>
    public IBlock UpdateShape(ShapeUpdateContext context) => context.State;

    /// <summary>
    /// Runs a tick scheduled with <see cref="IBlockLevel.ScheduleTick"/> or
    /// <see cref="ILevel.ScheduleBlockTick"/>, like vanilla's <c>tick</c>, while the block is still there.
    /// </summary>
    public void Tick(BlockContext context)
    {
    }

    /// <summary>
    /// Runs a random tick, like vanilla's <c>randomTick</c>, for a block registered with
    /// <see cref="BlockProperties.RandomlyTicks"/>: the game rule's number of blocks a tick in each section near
    /// players, about once every 68 seconds for each block at the default speed.
    /// </summary>
    public void RandomTick(BlockContext context)
    {
    }

    /// <summary>
    /// Whether the block gives redstone power, like vanilla's <c>isSignalSource</c>: redstone dust connects to it
    /// from every side, and comparators read it from their sides. The same for every state of the block.
    /// </summary>
    public bool IsSignalSource(IBlock state) => false;

    /// <summary>
    /// The weak redstone power the block gives the neighbour asking, from 0 to 15, like vanilla's <c>getSignal</c>
    /// (see <see cref="ISignalReader"/>). Runs whenever a neighbour reads its power, so keep it a quick read of the
    /// state; when it changes, tell the neighbours with <see cref="IBlockLevel.UpdateNeighbors"/>.
    /// </summary>
    public int GetSignal(SignalContext context) => 0;

    /// <summary>
    /// The strong redstone power the block gives the neighbour asking, which also powers through that neighbour when
    /// it's a conductor, like vanilla's <c>getDirectSignal</c>. Tell the neighbours of that neighbour too when it
    /// changes.
    /// </summary>
    public int GetDirectSignal(SignalContext context) => 0;

    /// <summary>
    /// Whether comparators read the block's <see cref="GetAnalogOutput"/>, like vanilla's
    /// <c>hasAnalogOutputSignal</c>.
    /// </summary>
    public bool HasAnalogOutput(IBlock state) => false;

    /// <summary>
    /// The power, from 0 to 15, a comparator facing away from the block reads from it, like vanilla's
    /// <c>getAnalogOutputSignal</c>: how full a container is, say. When it changes other than with the block's
    /// state, tell comparators with <see cref="IBlockLevel.UpdateAnalogOutput"/>.
    /// </summary>
    public int GetAnalogOutput(SignalContext context) => 0;

    /// <summary>
    /// Called each tick an entity is inside the block (its box overlaps the block's cube), like vanilla's
    /// <c>entityInside</c>.
    /// </summary>
    public ValueTask EntityInsideAsync(EntityBlockContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called each tick an entity stands on the block, like vanilla's <c>stepOn</c>.
    /// </summary>
    public ValueTask SteppedOnAsync(EntityBlockContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// The block's items as another block reaches them through <see cref="ItemAccessContext.Side"/>, for a block
    /// that holds items without a block entity (like vanilla's composter), or that answers for its block entity.
    /// Null, the default, leaves it to the block entity (see <see cref="IItemAccessProvider"/>).
    /// </summary>
    /// <remarks>
    /// The server calls the access it returns inside calls into the plugin, from any thread: make it thread-safe.
    /// </remarks>
    public IItemAccess? GetItemAccess(ItemAccessContext context) => null;
}

/// <summary>
/// A player using a block.
/// </summary>
public sealed class BlockUseContext
{
    public required IPlayer Player { get; init; }

    public required Vector Position { get; init; }

    /// <summary>
    /// The block's state.
    /// </summary>
    public required IBlock Block { get; init; }

    /// <summary>
    /// The face of the block the player aimed at.
    /// </summary>
    public required BlockFace Face { get; init; }

    public required InteractionHand Hand { get; init; }

    /// <summary>The use packet's hit location relative to the block's lowest corner.</summary>
    public VectorD ClickedAt { get; init; }
}

/// <summary>A player's attempt to break a plugin block, before removal and drops.</summary>
public sealed class BlockBreakContext
{
    public required IPlayer Player { get; init; }

    public required IBlock Block { get; init; }

    public required BlockShapeHit Hit { get; init; }

    public bool IsCancelled { get; set; }
}
