using Obsidian.API.Inventory;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Events;

public sealed class ContainerClickEventArgs : ContainerEventArgs, ICancellable
{
    /// <summary>
    /// Gets the current item that was clicked
    /// </summary>
    public ItemStack Item => this.Container.GetItem(this.ClickedSlot);

    public bool IsPlayerInventory => this.ContainerId == 0;

    public required int ContainerId { get; init; }

    /// <summary>
    /// Gets the slot that was clicked
    /// </summary>
    public required int ClickedSlot { get; init; }

    public required sbyte Button { get; init; }

    public required int StateId { get; init; }

    public required ClickType ClickType { get; init; }

    public ItemStack? CarriedItem { get; init; }

    public required IReadOnlyDictionary<short, ItemStack?> ChangedSlots { get; init; }

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
