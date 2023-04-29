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

    public bool Cancel { get; set; }

    [SetsRequiredMembers]
    internal ContainerClickEventArgs(IPlayer player, BaseContainer container, ItemStack item) : base(player)
    {
        this.Container = container;
        this.Item = item;
    }
}
