namespace Obsidian.API.Events;

public class PlayerLeaveEventArgs : PlayerEventArgs
{
    public override string Name => "PlayerLeave";

    /// <summary>
    /// The date the player left.
    /// </summary>
    public DateTimeOffset LeaveDate { get; }

    public PlayerLeaveEventArgs(IPlayer player, IServer server, DateTimeOffset leave) : base(player, server)
    {
        this.LeaveDate = leave;
    }
}
