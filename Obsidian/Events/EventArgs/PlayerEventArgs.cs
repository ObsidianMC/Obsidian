using Obsidian.Entities;

namespace Obsidian.Events.EventArgs
{
    public class PlayerEventArgs : BaseMinecraftEventArgs
    {
        /// <summary>
        /// The player involved in this event
        /// </summary>
        public Player Player { get; }

        internal PlayerEventArgs(Player who) : base(who.client.Server)
        {
            this.Player = who;
        }
    }
}
