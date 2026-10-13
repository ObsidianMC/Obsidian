using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Events;

public partial class MainEventHandler
{
    /// <summary>
    /// Shift-clicks a stack in an enchanting table's window like vanilla's enchantment menu: from the table to the
    /// player's inventory, filling from the hotbar's last slot; lapis lazuli to the table's second slot; anything
    /// else, one item, to its empty first slot.
    /// </summary>
    private static bool MoveEnchantingItem(IPlayer player, EnchantmentTable table, BaseContainer source, int slot)
    {
        var inventory = player.Inventory;

        if (source == table)
            return MoveStack(player, table, slot, inventory, 9, 45, reverse: true);

        var item = inventory.GetItem(slot);
        if (item.IsNullOrAir())
            return false;

        if (item.Is(ItemsRegistry.LapisLazuli))
            return MoveStack(player, inventory, slot, table, 1, 2, reverse: true);

        if (!table.GetItem(0).IsNullOrAir())
            return false;

        table.SetItem(0, new ItemStack(item, 1));
        inventory.RemoveItem(slot, 1);

        return true;
    }

    internal static async ValueTask ReturnEnchantingItemsAsync(IPlayer player, EnchantmentTable table)
    {
        for (var slot = 0; slot < 2; slot++)
        {
            if (table.GetItem(slot) is not { Count: > 0 } item) continue;
            StoreCraftingItem(player.Inventory, item, 9, 45);
            SpawnThrownItem(player, item);
            table.RemoveItem(slot);
        }
        if (player.CarriedItem is { Count: > 0 } carried)
        {
            StoreCraftingItem(player.Inventory, carried, 9, 45);
            SpawnThrownItem(player, carried);
            player.CarriedItem = null;
        }
        await player.QueuePacketAsync(new ContainerSetContentPacket(0, player.Inventory.ToList()));
    }
}
