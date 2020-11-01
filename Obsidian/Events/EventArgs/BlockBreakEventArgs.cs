using Obsidian.API.Events;
using Obsidian.Blocks;
using Obsidian.Entities;

namespace Obsidian.Events.EventArgs
{
    public class BlockBreakEventArgs : PlayerEventArgs, ICancellable
    {
        /// <summary>
        /// The player who broke the block
        /// </summary>
        public new Player Player { get; set; }

        /// <summary>
        /// The block that was broken
        /// </summary>
        public Block Block { get; }

        public bool Cancel { get; set; }

        internal BlockBreakEventArgs(Player player) : base(player) { }
    }
}
