namespace Obsidian.API.Events;
public sealed class ContainerClosedEventArgs : ContainerEventArgs, ICancellable
{
    public static new string Name => "ContainerClosed";

    public bool IsCancelled { get; private set; }

    internal ContainerClosedEventArgs(IPlayer player, IServer server) : base(player, server)
    {
    }

    public void Cancel() => this.IsCancelled = true;
}
