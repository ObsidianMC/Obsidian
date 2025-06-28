using Obsidian.API.Inventory;

namespace Obsidian.API.Events;

public sealed class ContainerClickEventArgs : ContainerEventArgs, ICancellable
{
    /// <summary>
    /// Gets the current item that was clicked. />
    /// </summary>
    public ItemStack? Item => this.Container.GetItem(this.ClickedSlot);

    public bool IsPlayerInventory => this.ContainerId == 0;

    /// <summary>
    /// The ID of the inventory. Usually incremented by one every time an inventory is opened and resets when it reaches 255. 
    /// </summary>
    /// <remarks>
    /// This value is unique per client.
    /// </remarks>
    public required int ContainerId { get; init; }

    /// <summary>
    /// Gets the slot that was clicked
    /// </summary>
    public required int ClickedSlot { get; init; }

    /// <summary>
    /// The button that was clicked in the inventory. Can vary depending on the inventory type.
    /// </summary>
    public required sbyte Button { get; init; }

    public required int StateId { get; init; }

    public required ClickType ClickType { get; init; }

    /// <inheritdoc />
    public bool IsCancelled { get; private set; }

    internal ContainerClickEventArgs(IPlayer player, IServer server, BaseContainer container) : base(player, server, container) { }

    /// <inheritdoc />
    public void Cancel()
    {
        IsCancelled = true;
    }
}
