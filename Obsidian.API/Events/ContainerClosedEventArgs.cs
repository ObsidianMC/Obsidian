namespace Obsidian.API.Events;

/// <summary>
/// A player's open container closing, by the player or the server. A handler may open another container for the
/// player instead; the server then closes only the one this event is about.
/// </summary>
public sealed class ContainerClosedEventArgs : ContainerEventArgs, ICancellable
{
    /// <inheritdoc />
    /// <remarks>
    /// A cancelled close skips the server's handling, such as returning items left in a crafting grid to the player.
    /// </remarks>
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

    /// <inheritdoc />
    public void Cancel() => this.IsCancelled = true;
}
