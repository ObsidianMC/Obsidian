using Obsidian.API.Inventory;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    // Capture identities and versions under the inventory lock; hooks can replace a stack or edit it in place.
    private (ItemStack? Stack, long Version)[] CaptureInventoryItems()
    {
        var before = new (ItemStack? Stack, long Version)[this.Inventory.Size];
        for (var slot = 0; slot < before.Length; slot++)
        {
            var stack = this.Inventory.GetItem(slot);
            before[slot] = (stack, stack?.Version ?? 0);
        }

        return before;
    }

    private List<ClientboundPacket> CaptureInventoryChanges((ItemStack? Stack, long Version)[] before)
    {
        List<ClientboundPacket> updates = [];
        for (var slot = 0; slot < before.Length; slot++)
        {
            var stack = this.Inventory.GetItem(slot);
            if (!ReferenceEquals(stack, before[slot].Stack) || (stack?.Version ?? 0) != before[slot].Version)
                this.CaptureInventorySlot(slot, updates);
        }

        return updates;
    }

    // Both views must change together: a later open-window update copies its player slots back to window 0.
    // The caller holds the inventory lock, and sends these isolated packets through WindowSnapshots afterwards.
    private void CaptureInventorySlot(int slot, List<ClientboundPacket> updates)
    {
        var stack = this.Inventory.GetItem(slot);
        var copy = stack is null ? null : NetworkBuffer.SnapshotItemStack(stack);
        updates.Add(new ContainerSetSlotPacket
        {
            ContainerId = 0,
            Slot = (short)slot,
            SlotData = copy,
            StateId = this.Inventory.StateId++
        });

        if (this.OpenedContainer is BaseContainer window && slot is >= 9 and < 45)
        {
            updates.Add(new ContainerSetSlotPacket
            {
                ContainerId = this.CurrentContainerId,
                Slot = (short)(window.Size + slot - 9),
                SlotData = copy is null ? null : NetworkBuffer.SnapshotItemStack(copy),
                StateId = window is Container container ? container.StateId++ : 0
            });
        }
    }

    /// <summary>
    /// Runs an item use hook and sends inventory changes, even if the hook declines the use or throws.
    /// </summary>
    internal async ValueTask<bool> RunItemUseAsync(Func<ValueTask<bool>> use)
    {
        (ItemStack? Stack, long Version)[] before;
        using (await this.Inventory.LockAsync())
            before = this.CaptureInventoryItems();

        // Use hooks may change the world and take its tick lock, so don't hold the inventory lock across them.
        try
        {
            return await use();
        }
        finally
        {
            List<ClientboundPacket> updates;
            ClickWindowState state;
            using (await this.Inventory.LockAsync())
            {
                state = ClickWindowState.Of(this);
                updates = this.CaptureInventoryChanges(before);
            }

            if (updates.Count > 0)
                await this.WindowSnapshots.SendChangeAsync(this, state, updates);
        }
    }
}
