using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Items;

namespace Obsidian.Events.EventArgs
{
    public class InventoryClickEventArgs : PlayerEventArgs, ICancellable
    {
        /// <summary>
        /// Gets the clicked inventory
        /// </summary>
        public Inventory Inventory { get; }

        /// <summary>
        /// Gets the inventory type
        /// </summary>
        public InventoryType Type => this.Inventory.Type;


        /// <summary>
        /// Gets the current item that was clicked
        /// </summary>
        public ItemStack Item { get; set; }

        /// <summary>
        /// Gets the slot that was clicked
        /// </summary>
        public int Slot { get; set; }

        public bool Cancel { get; set; }

        internal InventoryClickEventArgs(Player player, Inventory inventory) : base(player) 
        {
            this.Inventory = inventory;
        }

    }
}
