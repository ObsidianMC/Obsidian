using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Entities;

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
                await args.Player.QueuePacketAsync(new Obsidian.Net.Packets.Play.Clientbound.ContainerSetContentPacket(0, args.Player.Inventory.ToList())
                { StateId = args.StateId + 1, CarriedItem = args.Player.CarriedItem });
                return;
            }
        }

        if (await HandleEnchantingClickAsync(args)) return;

        if (await HandleCraftingAsync(args))
            return;

        HandleClick(args, dropThrown: true);
    }

    /// <summary>
    /// Handles a click in a container without special slots, like a chest's or a plugin's menu.
    /// </summary>
    /// <param name="args">The click.</param>
    /// <param name="dropThrown">Whether items thrown from a slot drop in the world.</param>
    private static void HandleClick(ContainerClickEventArgs args, bool dropThrown)
    {
        // Like vanilla, any other click while dragging only cancels the drag.
        if (args.ClickType != ClickType.QuickCraft && args.Player.IsDragging)
        {
            ResetDrag(args.Player);
            return;
        }

        switch (args.ClickType)
        {
            case ClickType.Pickup:
                HandlePickup(args);
                break;
            case ClickType.Swap:
                HandleSwap(args);
                break;
            case ClickType.Throw:
                HandleThrow(args, !dropThrown);
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
        var player = args.Player;
        var source = args.Container;

        if (args.Item.IsNullOrAir())
            return;

        // Like vanilla's chest menu: the container's stack goes to the player's main inventory and hotbar, filling
        // from the hotbar's last slot, and the player's goes to the container, filling from its first slot.
        if (source != player.Inventory)
            MoveStack(source, args.ClickedSlot, player.Inventory, 9, 45, reverse: true);
        else if (player.OpenedContainer is BaseContainer opened && opened != player.Inventory)
            MoveStack(source, args.ClickedSlot, opened, 0, opened.Size, reverse: false);
    }

    /// <summary>
    /// Moves the stack in <paramref name="fromSlot"/> into slots <paramref name="start"/> (inclusive) to
    /// <paramref name="end"/> (exclusive) of <paramref name="to"/>, like vanilla's <c>moveItemStackTo</c>: it tops up
    /// the stacks of the same item first, then takes the first empty slot, walking the range backwards when
    /// <paramref name="reverse"/>. What doesn't fit stays.
    /// </summary>
    private static void MoveStack(
        BaseContainer from,
        short fromSlot,
        BaseContainer to,
        int start,
        int end,
        bool reverse)
    {
        var stack = from.GetItem(fromSlot);
        if (stack.IsNullOrAir())
            return;

        var slots = Enumerable.Range(start, end - start);
        if (reverse)
            slots = slots.Reverse();

        if (stack.MaxStackSize > 1)
        {
            foreach (var slot in slots)
            {
                var target = to.GetItem(slot);
                if (target.IsNullOrAir() || target != stack || target.Count >= stack.MaxStackSize)
                    continue;

                var moved = Math.Min(stack.MaxStackSize - target.Count, stack.Count);
                target.Count += moved;
                stack.Count -= moved;
                to.SetItem(slot, target);

                if (stack.Count == 0)
                {
                    from.RemoveItem(fromSlot);
                    return;
                }
            }
        }

        foreach (var slot in slots)
        {
            if (!to.GetItem(slot).IsNullOrAir())
                continue;

            // The stack itself moves, with its components.
            to.SetItem(slot, stack);
            from.RemoveItem(fromSlot);
            return;
        }

        from.SetItem(fromSlot, stack);
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

    /// <summary>
    /// Handles dragging the carried stack across slots, like vanilla's quick craft. A drag starts, adds slots and ends,
    /// in that order (the button's low two bits: 0, 1 and 2); the button's next two bits say how it spreads the stack
    /// as it starts: 0 evenly, 1 one item per slot and 2, for creative players only, a full stack per slot. Anything
    /// out of order, or without a carried stack, cancels it.
    /// </summary>
    /// <remarks>
    /// Slots are kept as the window numbers them, since a drag crosses from the container into the player's inventory,
    /// and resolved again when it ends.
    /// </remarks>
    private static void HandleQuickCraft(ContainerClickEventArgs args)
    {
        var player = args.Player;
        var carried = player.CarriedItem;
        var step = args.Button & 3;
        var inOrder = player.IsDragging ? step is 1 or 2 : step == 0;

        if (!inOrder || carried.IsNullOrAir())
        {
            ResetDrag(player);
            return;
        }

        if (step == 0)
        {
            var kind = (args.Button >> 2) & 3;
            if (kind is 0 or 1 || kind == 2 && player.GameMode == GameMode.Creative)
            {
                player.IsDragging = true;
                player.DragStart = (DraggingState)(kind << 2);
                player.DraggedSlots.Clear();
            }
            else
                ResetDrag(player);

            return;
        }

        var fullStacks = player.DragStart == DraggingState.StartMiddle;
        if (step == 1)
        {
            // Spreading evenly or one by one needs an item for each slot.
            var enough = fullStacks || carried.Count > player.DraggedSlots.Count;
            if (enough
                && !player.DraggedSlots.Contains(args.WindowSlot)
                && TryGetDragTarget(player, args.WindowSlot, carried, out _, out _))
            {
                player.DraggedSlots.Add(args.WindowSlot);
            }

            return;
        }

        EndDrag(args, carried);
    }

    private static void EndDrag(ContainerClickEventArgs args, ItemStack carried)
    {
        var player = args.Player;
        var start = player.DragStart;
        var slots = player.DraggedSlots.ToArray();

        ResetDrag(player);

        if (slots.Length == 1)
        {
            // Like vanilla, a drag over one slot is a click on it, which places nothing for full stacks.
            if (start != DraggingState.StartMiddle
                && TryGetDragTarget(player, slots[0], carried, out var container, out var slot))
            {
                var button = (sbyte)(start == DraggingState.StartLeft ? 0 : 1);
                HandlePickup(ContainerClickEventArgs.Create(
                    player,
                    args.Server,
                    container,
                    args.ContainerId,
                    slot,
                    button,
                    args.StateId,
                    ClickType.Pickup));
            }

            return;
        }

        if (slots.Length == 0 || start != DraggingState.StartMiddle && carried.Count < slots.Length)
            return;

        // Vanilla's spread: each slot is topped up by its share, up to a full stack, and the cursor keeps the rest.
        var share = start switch
        {
            DraggingState.StartLeft => carried.Count / slots.Length,
            DraggingState.StartRight => 1,
            _ => carried.MaxStackSize
        };
        var remaining = carried.Count;

        foreach (var windowSlot in slots)
        {
            if (!TryGetDragTarget(player, windowSlot, carried, out var container, out var slot))
                continue;

            var target = container.GetItem(slot);
            var existing = target.IsNullOrAir() ? 0 : target.Count;
            var count = Math.Min(share + existing, carried.MaxStackSize);

            remaining -= count - existing;
            container.SetItem(slot, new ItemStack(carried, count));
        }

        if (remaining > 0)
            carried.Count = remaining;
        else
            player.CarriedItem = null;
    }

    /// <summary>
    /// Finds a dragged-over slot, as the open window numbers it, in its container, if the carried stack can be
    /// dragged onto it: the slot is empty or holds the same item.
    /// </summary>
    private static bool TryGetDragTarget(
        IPlayer player,
        short windowSlot,
        ItemStack carried,
        out BaseContainer container,
        out short slot)
    {
        var window = player.OpenedContainer ?? player.Inventory;
        (slot, var forPlayer) = window.GetSlot(windowSlot);
        container = forPlayer ? player.Inventory : window;

        // A container's window shows the player's main inventory and hotbar, not their armour or crafting grid.
        if (windowSlot < 0 || slot < 0 || slot >= container.Size || forPlayer && slot is < 9 or >= 45)
            return false;

        var target = container.GetItem(slot);

        return target.IsNullOrAir() || target == carried;
    }

    private static void ResetDrag(IPlayer player)
    {
        player.IsDragging = false;
        player.DraggedSlots.Clear();
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

    internal static void OnInventoryClickTest(ContainerClickEventArgs args) => HandleClick(args, dropThrown: false);
}
