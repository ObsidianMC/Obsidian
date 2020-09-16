using Obsidian.Net.Packets;

namespace Obsidian.Events.EventArgs
{
    public class PacketReceivedEventArgs : AsyncEventArgs
    {
        /// <summary>
        /// The client that invoked the event
        /// </summary>
        public Client Client { get; set; }

        /// <summary>
        /// Packet received to invoke this event
        /// </summary>
        public Packet EventPacket { get; private set; }

        internal PacketReceivedEventArgs(Client client, Packet receivedPacket)
        {
            this.Client = client;
            this.EventPacket = receivedPacket;
        }
    }
}
