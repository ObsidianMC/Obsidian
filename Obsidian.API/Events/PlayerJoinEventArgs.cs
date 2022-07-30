namespace Obsidian.API.Events;

public sealed class PlayerJoinEventArgs : BaseEventArgs, IPlayerEvent
{
    /// <summary>
    /// The date the player joined.
    /// </summary>
    public DateTimeOffset JoinDate { get; }

    public PlayerJoinEventArgs(IPlayer player, DateTimeOffset join)
    {
        Player = player;
        JoinDate = join;
    }

    public IPlayer Player { get; }
}
