namespace Obsidian.API.Events;

/// <summary>
/// A player breaking a block, raised when they finish digging it, or start digging one they break at once (in
/// creative, or with a fast enough tool). Cancelling it keeps the block in place and restores it on the client.
/// </summary>
public class BlockBreakEventArgs : BlockEventArgs, ICancellable
{
    /// <summary>
    /// Player that has broken the block.
    /// </summary>
    public IPlayer Player { get; }

    /// <inheritdoc/>
    public bool IsCancelled { get; private set; }

    internal BlockBreakEventArgs(IServer server, IPlayer player, IBlock block, Vector location, ILevel level) : base(server, block, location, level)
    {
        Player = player;
    }

    /// <inheritdoc />
    public void Cancel()
    {
        IsCancelled = true;
    }
}
