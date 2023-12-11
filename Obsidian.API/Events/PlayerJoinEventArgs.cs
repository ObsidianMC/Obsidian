﻿namespace Obsidian.API.Events;

public class PlayerJoinEventArgs : PlayerEventArgs
{
    /// <summary>
    /// The date the player joined.
    /// </summary>
    public DateTimeOffset JoinDate { get; }

    public PlayerJoinEventArgs(IPlayer player, IServer server, DateTimeOffset join) : base(player, server)
    {
        this.JoinDate = join;
    }
}
