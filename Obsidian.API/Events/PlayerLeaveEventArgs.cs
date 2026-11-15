namespace Obsidian.API.Events;

/// <summary>
/// A player leaving the server, raised after they stop ticking and are removed from their level.
/// </summary>
public class PlayerLeaveEventArgs(IPlayer player, IServer server, DateTimeOffset leave) : PlayerEventArgs(player, server)
{
    /// <summary>
    /// The date the player left.
    /// </summary>
    public DateTimeOffset LeaveDate { get; } = leave;
}
