using Obsidian.API.Inventory;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;

namespace Obsidian.Entities;

public partial class Player
{
    /// <summary>
    /// Ticks the plugin items in the player's inventory that tick there (see
    /// <see cref="IItemBehavior.InventoryTickAsync"/>), then sends the slots they changed.
    /// </summary>
    /// <remarks>
    /// Taken under the inventory's lock, like a click, so that a hook never sees a click half done and a click
    /// checks the contents the hooks left (a click arriving meanwhile is refused and resent, its revision moved on).
    /// Nothing is locked or allocated while no stack in the inventory ticks.
    /// </remarks>
    private async ValueTask TickInventoryItemsAsync()
    {
        if (!ItemBehaviors.HasInventoryTicks(this.Inventory))
            return;

        List<ClientboundPacket> updates = [];
        ClickWindowState state;

        using (await this.Inventory.LockAsync())
        {
            var size = this.Inventory.Size;
            var before = new (ItemStack? Stack, long Version)[size];
            for (var slot = 0; slot < size; slot++)
            {
                var stack = this.Inventory.GetItem(slot);
                before[slot] = (stack, stack?.Version ?? 0);
            }

            await ItemBehaviors.TickInventoryAsync(this, this.Inventory, this.CurrentHeldItemSlot, this.Logger);

            for (var slot = 0; slot < size; slot++)
            {
                var stack = this.Inventory.GetItem(slot);
                if (ReferenceEquals(stack, before[slot].Stack) && (stack?.Version ?? 0) == before[slot].Version)
                    continue;

                updates.Add(new ContainerSetSlotPacket
                {
                    ContainerId = 0,
                    Slot = (short)slot,
                    SlotData = stack is null ? null : NetworkBuffer.SnapshotItemStack(stack),
                    StateId = this.Inventory.StateId++
                });
            }

            state = ClickWindowState.Of(this);
        }

        // Through plugins' packet handlers, after the lock; a slot changed meanwhile is sent later, with the window.
        if (updates.Count > 0)
            await this.WindowSnapshots.SendChangeAsync(this, state, updates);
    }
}
