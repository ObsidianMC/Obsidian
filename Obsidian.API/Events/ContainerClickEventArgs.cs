namespace Obsidian.API.Events
{
    public class ContainerClickEventArgs : PlayerEventArgs, ICancellable
    {
        /// <summary>
        /// Gets the clicked container
        /// </summary>
        public BaseContainer container { get; }

        /// <summary>
        /// Gets the current item that was clicked
        /// </summary>
        public ItemStack Item { get; }

        /// <summary>
        /// Gets the slot that was clicked
        /// </summary>
        public int Slot { get; set; }

        public bool Cancel { get; set; }

        internal ContainerClickEventArgs(IPlayer player, BaseContainer container, ItemStack item) : base(player)
        {
            this.container = container;
            this.Item = item;
        }
    }
}
