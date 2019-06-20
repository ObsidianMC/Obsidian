using Obsidian.Net.Packets;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.Events.EventArgs
{
    public class PacketReceivedEventArgs : BaseMinecraftEventArgs
    {
        /// <summary>
        /// Packet received to invoke this event
        /// </summary>
        public Packet EventPacket { get; private set; }

        internal PacketReceivedEventArgs(Client client, Packet receivedPacket) : base(client)
        {
            this.EventPacket = receivedPacket;
        }
    }
}
