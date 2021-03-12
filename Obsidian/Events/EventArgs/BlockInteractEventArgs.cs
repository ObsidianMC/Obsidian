using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.Entities;

namespace Obsidian.Events.EventArgs
{
    public class BlockInteractEventArgs : PlayerEventArgs, ICancellable
    {
        public bool Cancel { get; set; }

        /// <summary>
        /// The block that was interacted with.
        /// </summary>
        public Block Block { get; }

        public VectorF Position { get; }

        public BlockInteractEventArgs(Player who, Block block, VectorF location) : base(who)
        {
            this.Block = block;
            this.Position = location;
        }
    }
}
