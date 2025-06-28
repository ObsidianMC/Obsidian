namespace Obsidian.API.Events;
public sealed class ContainerClosedEventArgs : ContainerEventArgs, ICancellable
{
    public bool IsCancelled { get; private set; }

    internal ContainerClosedEventArgs(IPlayer player, IServer server, BaseContainer container) : base(player, server, container)
    {
    }

    public void Cancel() => this.IsCancelled = true;
}
