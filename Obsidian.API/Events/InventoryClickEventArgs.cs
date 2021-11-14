namespace Obsidian.API.Events;

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

    internal InventoryClickEventArgs(IPlayer player, Inventory inventory, ItemStack item) : base(player)
    {
        this.Inventory = inventory;
        this.Item = item;
    }
}
