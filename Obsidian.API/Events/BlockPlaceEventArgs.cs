namespace Obsidian.API.Events;

/// <summary>
/// A player placing a block, raised before the block is placed and the item used up. Cancelling it keeps the block
/// from being placed, and the player keeps the item.
/// </summary>
public sealed class BlockPlaceEventArgs : BlockEventArgs, ICancellable
{
    internal BlockPlaceEventArgs(
        IServer server,
        IPlayer player,
        IBlock block,
        Vector location,
        ILevel level,
        InteractionHand hand)
        : base(server, block, location, level)
    {
        this.Player = player;
        this.Hand = hand;
    }

    /// <summary>
    /// The player placing the block.
    /// </summary>
    public IPlayer Player { get; }

    /// <summary>
    /// The hand holding the block's item.
    /// </summary>
    public InteractionHand Hand { get; }

    /// <inheritdoc/>
    public bool IsCancelled { get; private set; }

    /// <inheritdoc/>
    public void Cancel() => this.IsCancelled = true;
}
