namespace Obsidian.API.Events;

public class PlayerLeaveEventArgs : PlayerEventArgs
{
    /// <summary>
    /// The date the player left.
    /// </summary>
    public DateTimeOffset LeaveDate { get; }

    public PlayerLeaveEventArgs(IPlayer player, DateTimeOffset leave) : base(player)
    {
        this.LeaveDate = leave;
    }
}
