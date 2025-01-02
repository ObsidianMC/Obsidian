using Obsidian.API.Inventory;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Events;

public sealed class ContainerClickEventArgs : ContainerEventArgs, ICancellable
{
    /// <summary>
    /// Gets the current item that was clicked
    /// </summary>
    public ItemStack Item { get; }

    /// <summary>
    /// Gets the slot that was clicked
    /// </summary>
    public int Slot { get; set; }

    /// <inheritdoc />
    public bool IsCancelled { get; private set; }

    [SetsRequiredMembers]
    internal ContainerClickEventArgs(IPlayer player, IServer server, BaseContainer container, ItemStack item) : base(player, server)
    {
        this.Container = container;
        this.Item = item;
    }

    /// <inheritdoc />
    public void Cancel()
    {
        IsCancelled = true;
    }
}
