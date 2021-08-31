namespace Obsidian.API.Events
{
    public class BlockBreakEventArgs : BlockEventArgs, ICancellable
    {
        /// <summary>
        /// Player that has broken the block. If the block wasn't broken by a player, value of this property will be null.
        /// </summary>
        public IPlayer? Player { get; }

        /// <inheritdoc/>
        public bool Cancel { get; set; }

        internal BlockBreakEventArgs(IServer server, IPlayer? player, Block block, Vector location) : base(server, block, location)
        {
            Player = player;
        }
    }
}
