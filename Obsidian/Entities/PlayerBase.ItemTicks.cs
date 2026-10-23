using Obsidian.API.Inventory;
using Obsidian.Net.Packets;
using Obsidian.Plugins;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    /// <summary>
    /// Ticks the plugin items in the player's inventory that tick there (see
    /// <see cref="IItemBehavior.InventoryTickAsync"/>), then sends the slots they changed.
    /// </summary>
    /// <remarks>
    /// Taken under the inventory's lock, like a click, so that a hook never sees a click half done and a click
    /// checks the contents the hooks left (a click arriving meanwhile is refused and resent, its revision moved on).
    /// What the hooks set off that takes the lock, like the armour a hurt wears or a death's drops, waits until the
    /// lock is let go and runs right after, in this tick (see <see cref="DeferredInventoryWork"/>). Nothing is locked
    /// or allocated while no stack in the inventory ticks.
    /// </remarks>
    internal async ValueTask TickInventoryItemsAsync()
    {
        if (!ItemBehaviors.HasInventoryTicks(this.Inventory))
            return;

        await using var transaction = DeferredInventoryWork.Begin(this.Logger);
        List<ClientboundPacket> updates;
        ClickWindowState state;

        using (await this.Inventory.LockAsync())
        {
            var before = this.CaptureInventoryItems();

            await ItemBehaviors.TickInventoryAsync(this, this.Inventory, this.CurrentHeldItemSlot, this.Logger);

            state = ClickWindowState.Of(this);
            updates = this.CaptureInventoryChanges(before);
        }

        // Through plugins' packet handlers, after the lock; a slot changed meanwhile is sent later, with the window.
        if (updates.Count > 0)
            await this.WindowSnapshots.SendChangeAsync(this, state, updates);
    }
}
