using Obsidian.Logging;

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
        //public Player Player => Client.Player;

        /// <summary>
        /// Console logger
        /// </summary>
        public AsyncLogger Logger => Server.Logger;

        /// <summary>
        /// Constructs a new BaseMinecraftEventArgs object.
        /// </summary>
        /// <param name="client">Client this event came from</param>
        /// <param name="packet">Packet received to invoke this event</param>
        internal BaseMinecraftEventArgs(Client client)
        {
            this.Client = client;
        }
    }
}
