namespace Obsidian.API.Events;

public sealed class ContainerClickEventArgs : BaseEventArgs, IPlayerEvent, ICancellable
{
    internal ContainerClickEventArgs(BaseContainer container, ItemStack item, int slot, IPlayer player)
    {
        Container = container;
        Item = item;
        Slot = slot;
        Player = player;
    }

    /// <summary>
    /// Gets the clicked container
    /// </summary>
    public BaseContainer Container { get; }

    /// <summary>
    /// Gets the current item that was clicked
    /// </summary>
    public ItemStack Item { get; }

    /// <summary>
    /// Gets the slot that was clicked
    /// </summary>
    public int Slot { get; set; }

    public IPlayer Player { get; }
    public bool Cancelled { get; set; }
}
