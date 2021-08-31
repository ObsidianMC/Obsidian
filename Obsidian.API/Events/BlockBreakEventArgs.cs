namespace Obsidian.API.Events
{
    public class BlockBreakEventArgs : PlayerEventArgs, ICancellable
    {
        /// <summary>
        /// The block that was broken.
        /// </summary>
        public Block Block { get; }

        /// <summary>
        /// Location of the broken block.
        /// </summary>
        public Vector Location { get; }

        /// <inheritdoc/>
        public bool Cancel { get; set; }

        internal BlockBreakEventArgs(IPlayer player, Block block, Vector location) : base(player)
        {
            Block = block;
            Location = location;
        }
    }
}
