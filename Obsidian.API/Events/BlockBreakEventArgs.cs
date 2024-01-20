namespace Obsidian.API.Events;

public class BlockBreakEventArgs : BlockEventArgs, ICancellable
{
    public override string Name => "BlockBreak";

    /// <summary>
    /// Player that has broken the block.
    /// </summary>
    public IPlayer Player { get; }

    /// <inheritdoc/>
    public bool IsCancelled { get; private set; }

    internal BlockBreakEventArgs(IServer server, IPlayer player, IBlock block, Vector location) : base(server, block, location)
    {
        Player = player;
    }

    /// <inheritdoc />
    public void Cancel()
    {
        IsCancelled = true;
    }
}
