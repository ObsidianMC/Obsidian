using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Events;

public partial class MainEventHandler
{
    private static async ValueTask<bool> HandleEnchantingClickAsync(ContainerClickEventArgs args)
    {
        if (args.Player.OpenedContainer is not EnchantmentTable table || args.Player is not Player player) return false;
        bool Accepts(int slot, ItemStack? item) => item.IsNullOrAir() || slot == 0 && item.Count == 1 || slot == 1 && item.Type == Material.LapisLazuli;
        if (args.ClickType == ClickType.QuickMove && args.ClickedSlot >= 0)
        {
            var item = args.Item;
            if (item is { Count: > 0 })
            {
                if (args.Container == table)
                {
                    StoreCraftingItem(player.Inventory, item, 9, 45);
                    if (item.Count <= 0) table.RemoveItem(args.ClickedSlot);
                }
                else
                {
                    var slot = item.Type == Material.LapisLazuli ? 1 : 0;
                    var existing = table.GetItem(slot);
                    var empty = existing.IsNullOrAir();
                    var room = slot == 0 ? empty ? 1 : 0 : empty ? item.MaxStackSize : existing == item ? existing.MaxStackSize - existing.Count : 0;
                    var count = Math.Min(item.Count, room);
                    if (count > 0)
                    {
                        table.SetItem(slot, new ItemStack(item, count + (empty ? 0 : existing.Count)));
                        args.Container.RemoveItem(args.ClickedSlot, count);
                    }
                }
            }
        }
        else if (args.ClickType == ClickType.Pickup)
        {
            if (args.Container != table || args.ClickedSlot < 0 || player.CarriedItem.IsNullOrAir() ||
                Accepts(args.ClickedSlot, new ItemStack(player.CarriedItem, args.Button == 1 ? 1 : player.CarriedItem.Count)))
                HandleCraftingPickup(args);
            else if (args.ClickedSlot == 0 && table.GetItem(0).IsNullOrAir())
            {
                table.SetItem(0, new ItemStack(player.CarriedItem));
                if (--player.CarriedItem.Count == 0) player.CarriedItem = null;
            }
        }
        else if (args.ClickType == ClickType.Swap && args.ClickedSlot >= 0)
        {
            var sourceSlot = args.Button == 40 ? 45 : args.Button is >= 0 and <= 8 ? 36 + args.Button : -1;
            if (sourceSlot >= 0 && (args.Container != table || Accepts(args.ClickedSlot, player.Inventory.GetItem(sourceSlot)))) HandleSwap(args);
        }
        else if (args.ClickType == ClickType.Throw) HandleCraftingThrow(args);
        else if (args.ClickType == ClickType.Clone) HandleClone(args);
        else if (args.ClickType == ClickType.PickupAll) HandlePickupAll(args);
        else if (args.ClickType == ClickType.QuickCraft) HandleEnchantingDrag(args, table);
        await player.RefreshEnchantingAsync();
        await player.Client.QueuePacketAsync(new ContainerSetContentPacket(args.ContainerId,
            table.Concat(player.Inventory.Skip(9).Take(36)).ToList()) { StateId = args.StateId + 1, CarriedItem = player.CarriedItem });
        return true;
    }

    private static void HandleEnchantingDrag(ContainerClickEventArgs args, EnchantmentTable table)
    {
        var player = args.Player;
        var carried = player.CarriedItem;
        if (args.Button is 0 or 4 or 8) { player.DraggedSlots.Clear(); return; }
        if (carried is not { Count: > 0 } || args.Button >= 8 && player.GameMode != GameMode.Creative) return;
        if (args.Button is 1 or 5 or 9)
        {
            if (args.ClickedSlot < 0 || args.Container == table &&
                (args.ClickedSlot == 1 && carried.Type != Material.LapisLazuli || args.ClickedSlot == 0 && !args.Item.IsNullOrAir())) return;
            var item = args.Item;
            if (!item.IsNullOrAir() && (item != carried || item.Count >= item.MaxStackSize)) return;
            var slot = args.Container == table ? args.ClickedSlot : (short)(table.Size + args.ClickedSlot - 9);
            if (!player.DraggedSlots.Contains(slot)) player.DraggedSlots.Add(slot);
            return;
        }
        if (args.Button is not (2 or 6 or 10) || player.DraggedSlots.Count == 0) return;
        var perSlot = args.Button == 2 ? carried.Count / player.DraggedSlots.Count : 1;
        foreach (var slot in player.DraggedSlots)
        {
            var container = slot < table.Size ? (BaseContainer)table : player.Inventory;
            var index = slot < table.Size ? slot : slot - table.Size + 9;
            if (index < 0 || index >= container.Size || container == player.Inventory && index is < 9 or >= 45) continue;
            if (container == table && index == 1 && carried.Type != Material.LapisLazuli) continue;
            var target = container.GetItem(index);
            if (!target.IsNullOrAir() && target != carried) continue;
            var maximum = container == table && index == 0 ? 1 : carried.MaxStackSize;
            var count = Math.Min(args.Button == 10 ? maximum : Math.Min(perSlot, carried.Count), maximum - (target?.Count ?? 0));
            if (count <= 0) continue;
            container.SetItem(index, new ItemStack(carried, count + (target?.Count ?? 0)));
            if (args.Button != 10) carried.Count -= count;
        }
        if (carried.Count == 0) player.CarriedItem = null;
        player.DraggedSlots.Clear();
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
        await player.Client.QueuePacketAsync(new ContainerSetContentPacket(0, player.Inventory.ToList()));
    }
}
