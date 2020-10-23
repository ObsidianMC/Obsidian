namespace Obsidian.Events.EventArgs
{
    public class BaseMinecraftEventArgs : AsyncEventArgs
    {
        /// <summary>
        /// Server this event took place in
        /// </summary>
        public Server Server { get; }

        /// <summary>
        /// Constructs a new BaseMinecraftEventArgs object.
        /// <param name="server">The server that's handling this event</param>
        internal BaseMinecraftEventArgs(Server server)
        {
            this.Server = server;
        }
    }
}
