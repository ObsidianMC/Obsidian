using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.Entities;

namespace Obsidian.Events.EventArgs
{
    public class InventoryClickEventArgs : PlayerEventArgs, ICancellable
    {
        /// <summary>
        /// Gets the clicked inventory
        /// </summary>
        public Inventory Inventory { get; }

        /// <summary>
        /// Gets the current item that was clicked
        /// </summary>
        public ItemStack Item { get; }

        /// <summary>
        /// Gets the slot that was clicked
        /// </summary>
        public int Slot { get; set; }

        public bool Cancel { get; set; }

        internal InventoryClickEventArgs(Player player, Inventory inventory, ItemStack item) : base(player)
        {
            this.Inventory = inventory;
            this.Item = item;
        }

    }
}
