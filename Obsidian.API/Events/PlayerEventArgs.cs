namespace Obsidian.API.Events;

/// <summary>
/// The base class for events about a player.
/// </summary>
public class PlayerEventArgs : BaseMinecraftEventArgs
{
    /// <summary>
    /// The player involved in this event.
    /// </summary>
    public IPlayer Player { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerEventArgs"/> class.
    /// </summary>
    /// <param name="player">The player involved in the event.</param>
    /// <param name="server">The server the event takes place in.</param>
    protected PlayerEventArgs(IPlayer player, IServer server) : base(server)
    {
        Player = player;
    }
}
