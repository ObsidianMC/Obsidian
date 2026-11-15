namespace Obsidian.API.Events;

/// <summary>
/// A player teleported to a new position, raised after the teleport is done and the client has been sent the new
/// position.
/// </summary>
public class PlayerTeleportEventArgs : PlayerEventArgs
{
    /// <summary>
    /// Gets the position the player was at before the teleport.
    /// </summary>
    public VectorD OldPosition { get; }
    /// <summary>
    /// Gets the position the player was teleported to.
    /// </summary>
    public VectorD NewPosition { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerTeleportEventArgs"/> class.
    /// </summary>
    /// <param name="player">The player who was teleported.</param>
    /// <param name="server">The server the event takes place in.</param>
    /// <param name="oldPosition">The position before the teleport.</param>
    /// <param name="newPosition">The position after the teleport.</param>
    public PlayerTeleportEventArgs(IPlayer player, IServer server, VectorD oldPosition, VectorD newPosition) : base(player, server)
    {
        OldPosition = oldPosition;
        NewPosition = newPosition;
    }
}
