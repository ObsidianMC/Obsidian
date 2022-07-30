namespace Obsidian.API.Events;

public sealed class PlayerLeaveEventArgs : BaseEventArgs, IPlayerEvent
{
    /// <summary>
    /// The date the player left.
    /// </summary>
    public DateTimeOffset LeaveDate { get; }

    public PlayerLeaveEventArgs(IPlayer player, DateTimeOffset leave)
    {
        Player = player;
        LeaveDate = leave;
    }

    public IPlayer Player { get; }
}
