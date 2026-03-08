namespace Obsidian.API.Events;

public class PlayerLeaveEventArgs(IPlayer player, IServer server, DateTimeOffset leave) : PlayerEventArgs(player, server)
{
    /// <summary>
    /// The date the player left.
    /// </summary>
    public DateTimeOffset LeaveDate { get; } = leave;
}
