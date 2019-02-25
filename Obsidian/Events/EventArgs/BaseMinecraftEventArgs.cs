using Obsidian.Entities;
using Obsidian.Logging;
using Obsidian.Packets;

namespace Obsidian.Events.EventArgs
{
    public class BaseMinecraftEventArgs : AsyncEventArgs
    {
        /// <summary>
        /// Client this event came from
        /// </summary>
        public Client Client { get; private set; }

        /// <summary>
        /// Server this event took place in
        /// </summary>
        public Server Server => Client.OriginServer;

        /// <summary>
        /// Player that invoked this event
        /// </summary>
        public MinecraftPlayer Player => Client.Player;

        /// <summary>
        /// Console logger
        /// </summary>
        public Logger Logger => Server.Logger;

        /// <summary>
        /// Packet received to invoke this event
        /// </summary>
        public Packet EventPacket { get; private set; }

        /// <summary>
        /// Constructs a new BaseMinecraftEventArgs object.
        /// </summary>
        /// <param name="client">Client this event came from</param>
        /// <param name="packet">Packet received to invoke this event</param>
        internal BaseMinecraftEventArgs(Client client, Packet packet)
        {
            this.Client = client;
            this.EventPacket = packet;
        }
    }
}
