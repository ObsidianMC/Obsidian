using Obsidian.API.Inventory;

namespace Obsidian.API.BlockEntities;

public partial class ContainerBlockEntity
{
    /// <summary>
    /// Locks this container's inventory against menu clicks for a synchronous part of its tick. Returns null when
    /// the container is unavailable, or the caller holds the level's tick lock.
    /// </summary>
    /// <remarks>
    /// Read and change <see cref="Inventory"/> directly until the handle is disposed. Do not await, call item access
    /// (which takes the same lock), or acquire other locks while holding it. Changes notify viewers and comparators
    /// on disposal. Pending loot is prepared before locking; retired inventories cannot be changed.
    /// </remarks>
    protected IDisposable? LockInventory()
    {
        var host = this.Host as IContainerItemsHost;
        if (host is { HoldsTickLock: true } || host is not null && !host.PrepareItems(this))
            return null;

        var held = this.Inventory.Lock();
        if (this.IsRemoved || this.Inventory.IsRetired || this.LootTable is not null)
        {
            held.Dispose();
            return null;
        }

        return new InventoryLock(this, host, held, this.Inventory.Revision);
    }

    private sealed class InventoryLock(
        ContainerBlockEntity owner,
        IContainerItemsHost? host,
        IDisposable held,
        long revision) : IDisposable
    {
        public void Dispose()
        {
            var changed = owner.Inventory.Revision != revision;
            held.Dispose();
            if (!changed)
                return;

            owner.SetChanged();
            host?.OnItemsChanged(owner);
        }
    }
}
