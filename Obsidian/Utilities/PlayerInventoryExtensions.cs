using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Utilities;

internal static class PlayerInventoryExtensions
{
    /// <summary>
    /// Sends one slot of the player's own inventory to their client after an interaction changed it.
    /// </summary>
    public static ValueTask SendInventorySlotAsync(this IPlayer player, int slot) =>
        player.QueuePacketAsync(new ContainerSetSlotPacket
        { ContainerId = 0, Slot = (short)slot, SlotData = player.Inventory.GetItem(slot) });
}
