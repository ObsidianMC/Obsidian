using Obsidian.Entities;
using System;

namespace Obsidian.Events.EventArgs
{
    public class PlayerJoinEventArgs : PlayerEventArgs
    {
        /// <summary>
        /// The date the player joined
        /// </summary>
        public DateTimeOffset JoinDate { get; }

        internal PlayerJoinEventArgs(Player player, DateTimeOffset join) : base(player)
        {
            this.JoinDate = join;
        }
    }
}
