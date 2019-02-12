using Obsidian.Connection;
using Obsidian.Packets;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.Events.EventArgs
{
    public class PlayerJoinEventArgs : BaseMinecraftEventArgs
    {
        DateTimeOffset JoinDateTime { get; }

        internal PlayerJoinEventArgs(Client client, Packet packet, DateTimeOffset join) : base(client, packet)
        {
            this.JoinDateTime = join;
        }
    }
}
