using Obsidian.Entities;
using Obsidian.Net.Packets;
using Obsidian.Util;

namespace Obsidian.Events.EventArgs
{
    public class BlockBreakEventArgs : BaseMinecraftEventArgs
    {
        /// <summary>
        /// Player who broke the block
        /// </summary>
        public Player Player { get; }



        internal BlockBreakEventArgs(Client client) : base(client)
        {
        }

        public Position Location { get; }
    }
}
