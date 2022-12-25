namespace Obsidian.API.Events;

public class BlockBreakEventArgs : BlockEventArgs, ICancellable
{
    /// <summary>
    /// Player that has broken the block.
    /// </summary>
    public IPlayer Player { get; }

    /// <inheritdoc/>
    public bool Cancel { get; set; }

    internal BlockBreakEventArgs(IServer server, IPlayer player, IBlock block, Vector location) : base(server, block, location)
    {
        Player = player;
    }
}
