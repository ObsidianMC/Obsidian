using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Diagnostics;

namespace Obsidian.Events;

public partial class MainEventHandler
{
    private static async ValueTask<bool> HandleCraftingAsync(ContainerClickEventArgs args)
    {
        var player = args.Player;
        BaseContainer grid;
        int width;
        if (args.ContainerId == 0)
        {
            grid = player.Inventory;
            width = 2;
        }
        else if (player.OpenedContainer is CraftingTable { Type: InventoryType.Crafting } table)
        {
            grid = table;
            width = 3;
        }
        else
            return false;

        RefreshCraftingResult(grid, width);
        if (args.Container == grid && args.ClickedSlot == 0)
            TakeCraftingResult(args, grid, width);
        else if (args.ClickType == ClickType.QuickMove)
            MoveCraftingItem(args, grid, width);
        else
        {
            switch (args.ClickType)
            {
                case ClickType.Pickup:
                    HandleCraftingPickup(args);
                    break;
                case ClickType.Swap when args.ClickedSlot >= 0:
                    HandleSwap(args);
                    break;
                case ClickType.Throw:
                    HandleCraftingThrow(args);
                    break;
                case ClickType.QuickCraft:
                    HandleCraftingDrag(args, grid);
                    break;
                case ClickType.PickupAll:
                    HandlePickupAll(args);
                    break;
                case ClickType.Clone:
                    HandleClone(args);
                    break;
            }
        }

        RefreshCraftingResult(grid, width);
        var items = grid.ToList();
        if (width == 3)
            items.AddRange(player.Inventory.Skip(9).Take(36));
        await player.Client.QueuePacketAsync(new ContainerSetContentPacket(args.ContainerId, items)
        {
            StateId = args.StateId + 1,
            CarriedItem = player.CarriedItem
        });
        if (width == 3 && args.ClickType == ClickType.Swap && args.Button == 40)
            await player.Client.QueuePacketAsync(new ContainerSetSlotPacket
            {
                ContainerId = -2,
                Slot = 45,
                SlotData = player.Inventory.GetItem(45)
            });
        return true;
    }

    private static void RefreshCraftingResult(BaseContainer grid, int width)
    {
        var result = RecipesRegistry.FindRecipe(grid, width)?.Result.FirstOrDefault();
        Debug.Assert(result is null || (result.Count > 0 && result.Count <= result.MaxStackSize),
            "Generated crafting results must fit in one stack.");
        grid.SetItem(0, result is null ? null : new ItemStack(result, result.Count));
    }

    private static void HandleCraftingPickup(ContainerClickEventArgs args)
    {
        if (args.Button is not (0 or 1))
            return;
        var player = args.Player;
        var carried = player.CarriedItem;
        if (args.ClickedSlot == OutsideInventory)
        {
            if (carried.IsNullOrAir() || carried.Count <= 0)
                return;
            int count = args.Button == 0 ? carried.Count : 1;
            SpawnThrownItem(player, new ItemStack(carried, count));
            carried.Count -= count;
            if (carried.Count == 0)
                player.CarriedItem = null;
            return;
        }

        var clicked = args.Item;
        if (carried.IsNullOrAir() || carried.Count <= 0)
        {
            if (clicked.IsNullOrAir() || clicked.Count <= 0)
                return;
            int count = args.Button == 0 ? clicked.Count : (clicked.Count + 1) / 2;
            player.CarriedItem = new ItemStack(clicked, count);
            args.Container.RemoveItem(args.ClickedSlot, count);
        }
        else if (clicked.IsNullOrAir() || clicked.Count <= 0 || clicked == carried)
        {
            int count = Math.Min(args.Button == 0 ? carried.Count : 1,
                carried.MaxStackSize - (clicked?.Count ?? 0));
            if (count <= 0)
                return;
            args.Container.SetItem(args.ClickedSlot, new ItemStack(carried, (clicked?.Count ?? 0) + count));
            carried.Count -= count;
            if (carried.Count == 0)
                player.CarriedItem = null;
        }
        else if (carried.Count <= carried.MaxStackSize)
        {
            args.Container.SetItem(args.ClickedSlot, carried);
            player.CarriedItem = clicked;
        }
    }

    private static void HandleCraftingThrow(ContainerClickEventArgs args)
    {
        if (args.ClickedSlot < 0 || args.Button is not (0 or 1) || args.Item.IsNullOrAir())
            return;
        var item = args.Item!;
        int count = args.Button == 0 ? 1 : item.Count;
        SpawnThrownItem(args.Player, new ItemStack(item, count));
        args.Container.RemoveItem(args.ClickedSlot, count);
    }

    private static void HandleCraftingDrag(ContainerClickEventArgs args, BaseContainer grid)
    {
        var player = args.Player;
        var carried = player.CarriedItem;
        if (args.Button is 0 or 4 or 8)
        {
            player.DraggedSlots.Clear();
            return;
        }
        if (carried.IsNullOrAir() || carried.Count <= 0 ||
            (args.Button >= 8 && player.GameMode != GameMode.Creative))
            return;

        if (args.Button is 1 or 5 or 9)
        {
            if (args.ClickedSlot < 0 || (args.Container == grid && args.ClickedSlot == 0))
                return;
            var item = args.Item;
            if (!item.IsNullOrAir() && (item != carried || item.Count >= item.MaxStackSize))
                return;
            short slot = args.Container == grid ? args.ClickedSlot : (short)(grid.Size + args.ClickedSlot - 9);
            if (!player.DraggedSlots.Contains(slot))
                player.DraggedSlots.Add(slot);
            return;
        }
        if (args.Button is not (2 or 6 or 10) || player.DraggedSlots.Count == 0)
            return;

        int perSlot = args.Button == 2 ? carried.Count / player.DraggedSlots.Count : 1;
        foreach (short slot in player.DraggedSlots)
        {
            var targetContainer = slot < grid.Size ? grid : player.Inventory;
            int targetSlot = slot < grid.Size ? slot : slot - grid.Size + 9;
            var target = targetContainer.GetItem(targetSlot);
            if (!target.IsNullOrAir() && target != carried)
                continue;
            int count = Math.Min(args.Button == 10 ? carried.MaxStackSize : Math.Min(perSlot, carried.Count),
                carried.MaxStackSize - (target?.Count ?? 0));
            if (count <= 0)
                continue;
            targetContainer.SetItem(targetSlot, new ItemStack(carried, (target?.Count ?? 0) + count));
            if (args.Button != 10)
                carried.Count -= count;
        }
        if (carried.Count == 0)
            player.CarriedItem = null;
        player.DraggedSlots.Clear();
        player.IsDragging = false;
    }

    private static void TakeCraftingResult(ContainerClickEventArgs args, BaseContainer grid, int width)
    {
        var player = args.Player;
        if (args.ClickType == ClickType.Clone)
        {
            HandleClone(args);
            return;
        }

        var initialResult = grid.GetItem(0);
        do
        {
            var result = grid.GetItem(0);
            if (result.IsNullOrAir() || result.Count <= 0 || result != initialResult)
                return;

            switch (args.ClickType)
            {
                case ClickType.Pickup when args.Button is 0 or 1:
                    if (!player.CarriedItem.IsNullOrAir())
                    {
                        if (player.CarriedItem != result || player.CarriedItem.Count + result.Count > player.CarriedItem.MaxStackSize)
                            return;
                        player.CarriedItem.Count += result.Count;
                    }
                    else
                        player.CarriedItem = new ItemStack(result, result.Count);
                    break;
                case ClickType.QuickMove:
                    if (!CanStoreCraftingItem(player.Inventory, result, 9, 45))
                        return;
                    StoreCraftingItem(player.Inventory, new ItemStack(result, result.Count), 9, 45);
                    break;
                case ClickType.Swap when args.Button is >= 0 and <= 8 or 40:
                    int slot = args.Button == 40 ? 45 : args.Button + 36;
                    var target = player.Inventory.GetItem(slot);
                    if (!target.IsNullOrAir() && (target != result || target.Count + result.Count > target.MaxStackSize))
                        return;
                    player.Inventory.SetItem(slot, new ItemStack(result, (target?.Count ?? 0) + result.Count));
                    break;
                case ClickType.Throw when args.Button is 0 or 1:
                    SpawnThrownItem(player, new ItemStack(result, result.Count));
                    break;
                default:
                    return;
            }

            ConsumeCraftingIngredients(player, grid, width);
            RefreshCraftingResult(grid, width);
        } while (args.ClickType == ClickType.QuickMove);
    }

    private static void ConsumeCraftingIngredients(IPlayer player, BaseContainer grid, int width)
    {
        for (int slot = 1; slot <= width * width; slot++)
        {
            var ingredient = grid.GetItem(slot);
            if (ingredient.IsNullOrAir() || ingredient.Count <= 0)
                continue;

            var remainder = ingredient.Type switch
            {
                Material.MilkBucket or Material.WaterBucket or Material.LavaBucket => ItemsRegistry.GetSingleItem(Material.Bucket),
                Material.HoneyBottle => ItemsRegistry.GetSingleItem(Material.GlassBottle),
                _ => null
            };
            grid.RemoveItem(slot, 1);
            if (remainder is null)
                continue;

            if (grid.GetItem(slot).IsNullOrAir())
                grid.SetItem(slot, remainder);
            else
            {
                StoreCraftingItem(player.Inventory, remainder, 9, 45);
                SpawnThrownItem(player, remainder);
            }
        }
    }

    private static void MoveCraftingItem(ContainerClickEventArgs args, BaseContainer grid, int width)
    {
        var item = args.Item;
        if (item.IsNullOrAir() || args.ClickedSlot < 0)
            return;

        if (width == 3 && args.Container == args.Player.Inventory)
            StoreCraftingItem(grid, item, 1, 10);
        else
        {
            int start = args.ClickedSlot >= 9 && args.ClickedSlot < 36 && args.Container == args.Player.Inventory ? 36 : 9;
            StoreCraftingItem(args.Player.Inventory, item, start,
                args.Container == grid && args.ClickedSlot <= width * width ? 45 : start == 36 ? 45 : 36);
        }

        if (item.Count == 0)
            args.Container.RemoveItem(args.ClickedSlot);
    }

    private static bool CanStoreCraftingItem(BaseContainer container, ItemStack item, int start, int end)
    {
        int space = 0;
        for (int slot = start; slot < end; slot++)
        {
            var target = container.GetItem(slot);
            if (target.IsNullOrAir())
                space += item.MaxStackSize;
            else if (target == item)
                space += Math.Max(0, target.MaxStackSize - target.Count);
        }
        return space >= item.Count;
    }

    private static void StoreCraftingItem(BaseContainer container, ItemStack item, int start, int end)
    {
        for (int pass = 0; pass < 2 && item.Count > 0; pass++)
        {
            for (int slot = start; slot < end && item.Count > 0; slot++)
            {
                var target = container.GetItem(slot);
                if (pass == 0 ? target.IsNullOrAir() || target != item : !target.IsNullOrAir())
                    continue;

                int amount = Math.Min(item.Count, target.IsNullOrAir() ? item.MaxStackSize : target.MaxStackSize - target.Count);
                if (amount <= 0)
                    continue;
                if (target.IsNullOrAir())
                    container.SetItem(slot, new ItemStack(item, amount));
                else
                    target.Count += amount;
                item.Count -= amount;
            }
        }
    }

    internal static async ValueTask ReturnCraftingItemsAsync(IPlayer player, BaseContainer grid, int width)
    {
        for (int slot = 1; slot <= width * width; slot++)
        {
            var item = grid.GetItem(slot);
            if (item.IsNullOrAir())
                continue;
            StoreCraftingItem(player.Inventory, item, 9, 45);
            SpawnThrownItem(player, item);
            grid.RemoveItem(slot);
        }
        grid.RemoveItem(0);
        if (!player.CarriedItem.IsNullOrAir())
        {
            StoreCraftingItem(player.Inventory, player.CarriedItem, 9, 45);
            SpawnThrownItem(player, player.CarriedItem);
            player.CarriedItem = null;
        }
        player.DraggedSlots.Clear();
        player.IsDragging = false;
        await player.Client.QueuePacketAsync(new ContainerSetContentPacket(0, player.Inventory.ToList()));
    }
}
