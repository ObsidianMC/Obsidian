namespace Obsidian.API.Events;

/// <summary>
/// A player joining the server, raised once they finish configuration and enter the game.
/// </summary>
public class PlayerJoinEventArgs : PlayerEventArgs
{
    /// <summary>
    /// The date the player joined.
    /// </summary>
    public DateTimeOffset JoinDate { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerJoinEventArgs"/> class.
    /// </summary>
    /// <param name="player">The player who joined.</param>
    /// <param name="server">The server the event takes place in.</param>
    /// <param name="join">When the player joined.</param>
    public PlayerJoinEventArgs(IPlayer player, IServer server, DateTimeOffset join) : base(player, server)
    {
        this.JoinDate = join;
    }
}
