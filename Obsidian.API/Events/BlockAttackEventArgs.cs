namespace Obsidian.API.Events;

/// <summary>The initial left-click on a block, before mining or other attack behavior.</summary>
public sealed class BlockAttackEventArgs(IServer server, IPlayer player, IBlock block, Vector location, ILevel level)
    : BlockEventArgs(server, block, location, level), ICancellable
{
    public IPlayer Player { get; } = player;
    public bool IsCancelled { get; private set; }
    public void Cancel() => this.IsCancelled = true;
}
