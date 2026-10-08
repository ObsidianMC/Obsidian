using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using System.Runtime.InteropServices;

namespace Obsidian.Events;

public partial class MainEventHandler
{
    private const int OutsideInventory = -999;

    [EventPriority(Priority = Priority.Internal)]
    public async ValueTask OnInventoryClick(ContainerClickEventArgs args)
    {
        if (args.IsCancelled)
            return;

        if (args.Container == args.Player.Inventory && args.ClickedSlot is >= 5 and <= 8 &&
            args.ClickType != ClickType.Clone)
        {
            var equipped = args.Item;
            var locked = args.Player.GameMode != GameMode.Creative && args.Player.Alive &&
                CombatItems.EnchantmentLevel(equipped, EnchantmentsRegistry.BindingCurse) > 0;
            var incoming = args.ClickType == ClickType.Pickup ? args.Player.CarriedItem :
                args.ClickType == ClickType.Swap && args.Button is >= 0 and <= 8 ? args.Player.Inventory.GetItem(36 + args.Button) :
                args.ClickType == ClickType.Swap && args.Button == 40 ? args.Player.GetOffHandItem() : null;
            if (locked || !incoming.IsNullOrAir() && CombatItems.PlayerArmorSlot(incoming) != args.ClickedSlot)
            {
                await args.Player.Client.QueuePacketAsync(new Obsidian.Net.Packets.Play.Clientbound.ContainerSetContentPacket(0, args.Player.Inventory.ToList())
                { StateId = args.StateId + 1, CarriedItem = args.Player.CarriedItem });
                return;
            }
        }

        if (await HandleEnchantingClickAsync(args)) return;

        if (await HandleCraftingAsync(args))
            return;

        switch (args.ClickType)
        {
            case ClickType.Pickup:
                HandlePickup(args);
                break;
            case ClickType.Swap:
                HandleSwap(args);
                break;
            case ClickType.Throw:
                HandleThrow(args);
                break;
            case ClickType.QuickCraft:
                HandleQuickCraft(args);
                break;
            case ClickType.PickupAll:
                HandlePickupAll(args);
                break;
            case ClickType.QuickMove:
                HandleQuickMove(args);
                break;
            case ClickType.Clone:
                HandleClone(args);
                break;
            default:
                break;
        }
    }

    private static void HandleQuickMove(ContainerClickEventArgs args)
    {
        var container = args.Container;
        var player = args.Player;
        var clickedSlot = args.ClickedSlot;
        var clickedItem = args.Item;

        if (clickedItem.IsNullOrAir())
            return;

        TryMoveItem(container, clickedSlot, player.OpenedContainer ?? player.Inventory);
    }

    private static bool TryMoveItem(BaseContainer from, short fromSlot, BaseContainer to)
    {
        var sourceItem = from.GetItem(fromSlot);
        if (sourceItem.IsNullOrAir())
            return false;

        for (short i = 0; i < to.Size; i++)
        {
            var targetItem = to.GetItem(i);
            if (targetItem.IsNullOrAir())
                continue;

            if (targetItem == sourceItem && targetItem.Count < targetItem.MaxStackSize)
            {
                int space = targetItem.MaxStackSize - targetItem.Count;
                int transfer = Math.Min(space, sourceItem.Count);

                targetItem += transfer;
                sourceItem -= transfer;

                if (sourceItem.Count <= 0)
                {
                    from.RemoveItem(fromSlot);
                    return true;
                }
            }
        }

        for (short i = 0; i < to.Size; i++)
        {
            var targetItem = to.GetItem(i);
            if (targetItem.IsNullOrAir())
            {
                to.SetItem(i, new ItemStack(sourceItem.Holder, sourceItem.Count));
                from.RemoveItem(fromSlot);
                return true;
            }
        }

        from.SetItem(fromSlot, sourceItem);
        return false;
    }

    private static void HandleClone(ContainerClickEventArgs args)
    {
        var player = args.Player;
        var clickedItem = args.Item;

        if (player.GameMode != GameMode.Creative || clickedItem.IsNullOrAir())
            return;

        player.CarriedItem = new ItemStack(clickedItem, clickedItem.MaxStackSize);
    }

    private static void HandlePickupAll(ContainerClickEventArgs args)
    {
        var container = args.Container;
        var player = args.Player;
        var carriedItem = player.CarriedItem;

        if (carriedItem.IsNullOrAir() || carriedItem.Count >= carriedItem.MaxStackSize)
            return;

        int amountNeeded = carriedItem.MaxStackSize - carriedItem.Count;
        if (amountNeeded == 0)
            return;

        for (int i = 0; i < container.Size; i++)
        {
            if (i == 0 && (container is CraftingTable || container == player.Inventory))
                continue;
            ItemStack? item = container[i];
            if (container == player.Inventory && i is >= 5 and <= 8 && player.GameMode != GameMode.Creative &&
                CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.BindingCurse) > 0) continue;
            if (item != carriedItem)
                continue;

            int amountTaken = Math.Min(item.Count, amountNeeded);
            item -= amountTaken;

            if (item.Count == 0)
                container.RemoveItem(i);

            carriedItem += amountTaken;
            amountNeeded -= amountTaken;

            if (amountNeeded == 0)
                break;
        }

        //Try the player inventory
        if (amountNeeded > 0 && !args.IsPlayerInventory)
        {
            for (int i = 0; i < player.Inventory.Size; i++)
            {
                if (i == 0)
                    continue;
                var item = player.Inventory.GetItem(i);
                if (i is >= 5 and <= 8 && player.GameMode != GameMode.Creative &&
                    CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.BindingCurse) > 0) continue;
                if (item != carriedItem)
                    continue;

                int amountTaken = Math.Min(item.Count, amountNeeded);
                item -= amountTaken;

                if (item.Count == 0)
                    player.Inventory.RemoveItem(i);

                carriedItem += amountTaken;
                amountNeeded -= amountTaken;

                if (amountNeeded == 0)
                    break;
            }
        }
    }

    private static void HandleQuickCraft(ContainerClickEventArgs args)
    {
        var container = args.Container;
        var player = args.Player;
        var clickedSlot = args.ClickedSlot;
        var state = (DraggingState)args.Button;

        if (!player.IsDragging && !player.CarriedItem.IsNullOrAir())
        {
            if (player.DraggedSlots.Count == 0 || state != DraggingState.EndLeft)
                return;

            int totalItems = player.CarriedItem.Count;
            int perSlot = totalItems / player.DraggedSlots.Count;
            //int remainder = totalItems % player.DraggedSlots.Count;

            var span = CollectionsMarshal.AsSpan(player.DraggedSlots);
            for (var i = 0; i < player.DraggedSlots.Count; i++)
            {
                var slotIndex = span[i];
                var item = container.GetItem(slotIndex);

                var amount = Math.Min(perSlot, player.CarriedItem.MaxStackSize - (item?.Count ?? 0));

                if (amount > 0)
                {
                    if (item.IsNullOrAir())
                        container.SetItem(slotIndex, new(player.CarriedItem, amount));
                    else
                        item += amount;

                    player.CarriedItem -= amount;
                }

                if (player.CarriedItem.Count <= 0)
                    break;
            }

            player.DraggedSlots.Clear();
            return;
        }

        switch (state)
        {
            case DraggingState.AddSlotLeft:
                if (!player.DraggedSlots.Contains(clickedSlot))
                    player.DraggedSlots.Add(clickedSlot);
                break;
            case DraggingState.AddSlotRight:
                var existingItem = container.GetItem(clickedSlot);

                if (existingItem.IsNullOrAir())
                {
                    container.SetItem(clickedSlot, new(player.CarriedItem, 1));
                    player.CarriedItem -= 1;
                }
                else if (existingItem == player.CarriedItem && existingItem.Count < existingItem.MaxStackSize)
                {
                    existingItem += 1;
                    player.CarriedItem -= 1;
                }

                break;
            case DraggingState.AddSlotMiddle:
                container.SetItem(clickedSlot, new(player.CarriedItem, player.CarriedItem.MaxStackSize));
                break;
            default:
                break;
        }
    }

    private static void HandleThrow(ContainerClickEventArgs args, bool test = false)
    {
        var clickedSlot = args.ClickedSlot;
        var container = args.Container;
        var player = args.Player;
        var button = args.Button;

        if (clickedSlot == OutsideInventory)
            return;

        var thrownItem = ThrowItem(player, container, clickedSlot, button);

        if (test)
            return;

        SpawnThrownItem(player, thrownItem);
    }

    internal static ItemStack? ThrowItem(IPlayer player, BaseContainer container, short clickedSlot, sbyte button, bool forPlayer = false)
    {
        var source = forPlayer ? player.CarriedItem : container.GetItem(clickedSlot);
        if (source.IsNullOrAir() || source.Count <= 0)
            return null;

        // Outside clicks drop the whole cursor stack on left click, and one item on right click.
        var count = forPlayer ? button == 0 ? source.Count : 1 : button == 0 ? 1 : source.Count;
        var dropped = new ItemStack(source, count);
        if (forPlayer)
        {
            source.Count -= count;
            if (source.Count == 0)
                player.CarriedItem = null;
        }
        else
            container.RemoveItem(clickedSlot, count);

        return dropped;
    }

    private static void SpawnThrownItem(IPlayer player, ItemStack? thrownItem)
    {
        if (thrownItem.IsNullOrAir() || thrownItem.Count <= 0)
            return;

        ItemEntity.Drop(player, thrownItem);
    }

    private static void HandlePickup(ContainerClickEventArgs args)
    {
        var clickedSlot = args.ClickedSlot;
        var container = args.Container;
        var player = args.Player;
        var clickedItem = args.Item;
        var button = args.Button;

        if (!player.CarriedItem.IsNullOrAir())
        {
            if (button == 0)
            {
                if (clickedSlot == OutsideInventory)
                {
                    var thrownItem = ThrowItem(player, container, clickedSlot, button, true);
                    SpawnThrownItem(player, thrownItem);
                    return;
                }

                if (HandlePickupDirectItem(container, clickedSlot, player))
                    return;


                container.SetItem(clickedSlot, player.CarriedItem);
                player.CarriedItem = clickedItem;
            }
            else if (button == 1)
            {
                if (clickedSlot == OutsideInventory)
                {
                    var thrownItem = ThrowItem(player, container, clickedSlot, button, true);
                    SpawnThrownItem(player, thrownItem);
                    return;
                }

                var itemInSlot = container.GetItem(clickedSlot);

                if (itemInSlot.IsNullOrAir())
                {
                    container.SetItem(clickedSlot, new(player.CarriedItem, 1));
                    player.CarriedItem -= 1;
                }
                else if (itemInSlot == player.CarriedItem && itemInSlot.Count < itemInSlot.MaxStackSize)
                {
                    itemInSlot += 1;
                    player.CarriedItem -= 1;
                }
            }
        }
        else if (!clickedItem.IsNullOrAir())
        {
            player.CarriedItem = clickedItem;
            container.RemoveItem(clickedSlot);
        }
    }

    /// <summary>
    /// Handles merging a carried item stack with a stack in a container slot.
    /// </summary>
    /// <returns>True if the items were successfully merged, false otherwise.</returns>
    private static bool HandlePickupDirectItem(BaseContainer container, short clickedSlot, IPlayer player)
    {
        var clickedItem = container.GetItem(clickedSlot);
        var carriedItem = player.CarriedItem;

        if (clickedItem.IsNullOrAir() || carriedItem.IsNullOrAir() || clickedItem != carriedItem)
            return false;

        int spaceAvailable = clickedItem.MaxStackSize - clickedItem.Count;
        if (spaceAvailable <= 0)
            return false;

        int amountToTransfer = Math.Min(spaceAvailable, carriedItem.Count);

        if (amountToTransfer > 0)
        {
            clickedItem += amountToTransfer;
            carriedItem -= amountToTransfer;

            if (carriedItem.Count <= 0)
                player.CarriedItem = null;

            return true;
        }

        return false;
    }

    private static void HandleSwap(ContainerClickEventArgs args)
    {
        var carriedItem = args.Item;
        var clickedSlot = args.ClickedSlot;
        var container = args.Container;
        var player = args.Player;
        var button = args.Button;

        var localSlot = button == 40 ? 45 : button + 36;
        if (button is not (>= 0 and <= 8 or 40))
            return;

        var currentItem = player.Inventory.GetItem(localSlot);

        if (!carriedItem.IsNullOrAir())
        {
            if (currentItem.IsNullOrAir())
                container.RemoveItem(clickedSlot);
            else
                container.SetItem(clickedSlot, currentItem);

            player.Inventory.SetItem(localSlot, carriedItem);

            return;
        }

        container.SetItem(clickedSlot, currentItem);

        player.Inventory.RemoveItem(localSlot);
    }

    internal static void OnInventoryClickTest(ContainerClickEventArgs args)
    {
        switch (args.ClickType)
        {
            case ClickType.Pickup:
                HandlePickup(args);
                break;
            case ClickType.Swap:
                HandleSwap(args);
                break;
            case ClickType.Throw:
                HandleThrow(args, true);
                break;
            case ClickType.QuickCraft:
                HandleQuickCraft(args);
                break;
            case ClickType.PickupAll:
                HandlePickupAll(args);
                break;
            case ClickType.QuickMove:
                HandleQuickMove(args);
                break;
            case ClickType.Clone:
                HandleClone(args);
                break;
            default:
                break;
        }
    }
}
