namespace Obsidian.API.Events;

public class BlockBreakEventArgs : BlockEventArgs, ICancellable
{
    /// <summary>
    /// Player that has broken the block.
    /// </summary>
    public IPlayer Player { get; }

    /// <inheritdoc/>
    public bool IsCancelled { get; private set; }

    internal BlockBreakEventArgs(IServer server, IPlayer player, IBlock block, Vector location, IWorld world) : base(server, block, location, world)
    {
        Player = player;
    }

    /// <inheritdoc />
    public void Cancel()
    {
        IsCancelled = true;
    }
}
