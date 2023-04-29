namespace Obsidian.API.Events;
public sealed class ContainerCloseEventArgs : ContainerEventArgs, ICancellable
{
    public bool Cancel { get; set; }

    internal ContainerCloseEventArgs(IPlayer player) : base(player)
    {
    }
}
