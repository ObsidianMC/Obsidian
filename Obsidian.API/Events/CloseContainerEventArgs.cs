namespace Obsidian.API.Events;
public sealed class CloseContainerEventArgs : ContainerEventArgs, ICancellable
{
    public bool Cancel { get; set; }

    internal CloseContainerEventArgs(IPlayer player) : base(player)
    {
    }
}
