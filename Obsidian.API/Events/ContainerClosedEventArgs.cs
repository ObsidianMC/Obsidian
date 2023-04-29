namespace Obsidian.API.Events;
public sealed class ContainerClosedEventArgs : ContainerEventArgs, ICancellable
{
    public bool Cancel { get; set; }

    internal ContainerClosedEventArgs(IPlayer player) : base(player)
    {
    }
}
