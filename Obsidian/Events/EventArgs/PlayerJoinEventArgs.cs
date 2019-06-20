using Obsidian.Entities;
using System;

namespace Obsidian.Events.EventArgs
{
    public class PlayerJoinEventArgs : BaseMinecraftEventArgs
    {
        public DateTimeOffset JoinDate { get; }

        public Player Joined { get; }

        internal PlayerJoinEventArgs(Client client, DateTimeOffset join) : base(client)
        {
            this.JoinDate = join;
            this.Joined = client.Player;
        }
    }
}
