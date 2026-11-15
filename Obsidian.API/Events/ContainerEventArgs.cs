namespace Obsidian.API.Events;

/// <summary>
/// The base class for events about a container a player has open.
/// </summary>
public class ContainerEventArgs : PlayerEventArgs
{
    /// <summary>
    /// Gets the container the event is about.
    /// </summary>
    public BaseContainer Container { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContainerEventArgs"/> class.
    /// </summary>
    /// <param name="player">The player who has the container open.</param>
    /// <param name="server">The server the event takes place in.</param>
    /// <param name="container">The container the event is about.</param>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> is <see langword="null"/>.</exception>
    protected ContainerEventArgs(IPlayer player, IServer server, BaseContainer container) : base(player, server)
    {
        this.Container = container ?? throw new ArgumentNullException(nameof(container), "Container cannot be null.");
    }
}
