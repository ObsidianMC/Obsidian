namespace Obsidian.API.Events;

/// <summary>
/// A player's open container closing, by the player or the server. A handler may open another container for the
/// player instead; the server then closes only the one this event is about.
/// </summary>
public sealed class ContainerClosedEventArgs : ContainerEventArgs, ICancellable
{
    public bool IsCancelled { get; private set; }

    /// <summary>
    /// The id of the window that is closing (see <see cref="IPlayer.CurrentContainerId"/>).
    /// </summary>
    internal int WindowId { get; }

    internal ContainerClosedEventArgs(IPlayer player, IServer server, BaseContainer container, int windowId)
        : base(player, server, container)
    {
        this.WindowId = windowId;
    }

    public void Cancel() => this.IsCancelled = true;
}
