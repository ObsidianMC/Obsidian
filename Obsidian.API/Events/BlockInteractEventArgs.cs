using Obsidian.API;
using Obsidian.API.Events;

namespace Obsidian.Events.EventArgs
{
    public class BlockInteractEventArgs : PlayerEventArgs, ICancellable
    {
        /// <summary>
        /// The block that was interacted with.
        /// </summary>
        public Block Block { get; }

        /// <summary>
        /// Location of interaction.
        /// </summary>
        public VectorF Location { get; }

        /// <inheritdoc/>
        public bool Cancel { get; set; }

        public BlockInteractEventArgs(IPlayer player, Block block, VectorF location) : base(player)
        {
            Block = block;
            Location = location;
        }
    }
}
