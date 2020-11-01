using System;

namespace Obsidian.API.Events
{
    public class PlayerJoinEventArgs : PlayerEventArgs
    {
        /// <summary>
        /// The date the player joined.
        /// </summary>
        public DateTimeOffset JoinDate { get; }

        public PlayerJoinEventArgs(IPlayer player, DateTimeOffset join) : base(player)
        {
            this.JoinDate = join;
        }
    }
}
