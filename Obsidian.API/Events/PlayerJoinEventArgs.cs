namespace Obsidian.API.Events;

/// <summary>
/// Raised when a player joins the game. Connected players have finished configuration; server-side players raise
/// it too, without a configuration phase.
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
