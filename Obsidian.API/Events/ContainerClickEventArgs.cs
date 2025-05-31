using Obsidian.API.Inventory;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Events;

public sealed class ContainerClickEventArgs : ContainerEventArgs, ICancellable
{
    /// <summary>
    /// Gets the current item that was clicked. Is also <see cref="CarriedItem" />
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

    /// <summary>
    /// The item that the player is carrying otherwise null or air.
    /// </summary>
    /// <remarks>
    /// This item does not carry components and only carries the hash of those components.
    /// You'll have to compare hashes to make sure the item wasn't modified on the client side.
    /// </remarks>
    public IHashedItemStack? CarriedItem { get; init; }

    /// <summary>
    /// The slots that were changed.
    /// </summary>
    /// <remarks>
    /// The items represented in this dictionary do not carry any components. 
    /// The hashes must be compared to make sure they weren't modified.
    /// </remarks>
    public required IReadOnlyDictionary<short, IHashedItemStack?> ChangedSlots { get; init; }

    /// <inheritdoc />
    public bool IsCancelled { get; private set; }

    [SetsRequiredMembers]
    internal ContainerClickEventArgs(IPlayer player, IServer server, BaseContainer container) : base(player, server)
    {
        this.Container = container;
    }

    /// <inheritdoc />
    public void Cancel()
    {
        IsCancelled = true;
    }
}
