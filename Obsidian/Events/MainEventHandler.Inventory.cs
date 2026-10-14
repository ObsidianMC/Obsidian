using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Events;

public partial class MainEventHandler
{
    private const int OutsideInventory = -999;

    // The player's inventory numbers its slots like vanilla's inventory menu: the crafting result, the 2x2 grid,
    // the armour (5 to 8), the main inventory (9 to 35), the hotbar (36 to 44) and the off hand.
    private const int OffhandSlot = 45;

    // The off hand, as vanilla's player inventory numbers it in the set-slot packet.
    private const int OffhandInventorySlot = 40;

    // How many times a click's window is sent, captured again whenever it changed before being sent, before it's
    // left to the player's tick (see SendWindowAsync).
    private const int MaxWindowSends = 2;

    /// <summary>
    /// Applies a player's click in their open window, after plugins' handlers had their say, and sends the window to
    /// its viewers.
    /// </summary>
    /// <remarks>
    /// Players sharing a container take turns: the click is applied inside the window's lock and the player's
    /// inventory's, which plugins' handlers run without, so the window is checked again inside them, as it may have
    /// closed or changed meanwhile. A click whose slots or cursor changed since it arrived, or whose crafting result no
    /// longer follows its grid, or that a handler cancelled, isn't applied; either way the player's window and cursor
    /// are sent again, so that the client's prediction is corrected. The packets are captured inside the lock and sent
    /// after it, since sending them runs plugins' packet handlers. A click in a plugin's menu also runs its plugin's
    /// code, and moves items through its item access slots as it ends (see <see cref="MenuClick"/>).
    /// </remarks>
    [EventPriority(Priority = Priority.Internal)]
    public async ValueTask OnInventoryClick(ContainerClickEventArgs args)
    {
        var logger = args.Player is IClientPlayer connected
            ? connected.Client.Logger
            : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        await using var transaction = DeferredInventoryWork.Begin(logger);
        var player = args.Player;
        var window = player.OpenedContainer ?? player.Inventory;
        WindowSync sync;
        var cookingExperience = 0;

        using (await BaseContainer.LockWindowAsync(window, player.Inventory))
        {
            // A dead player's clicks are refused: their death drops their items (see Player.LoseItemsAtDeathAsync).
            if (!IsOpen(player, window, args.ContainerId)
                || args.Container != window && args.Container != player.Inventory
                || player.Health <= 0)
            {
                return;
            }

            // A plugin's menu runs its plugin's code in the click, unless the plugin is unloading or the menu is no
            // longer valid for the player.
            using var menuClick = MenuClick.Start(player, window);
            if (menuClick is { Refused: true })
                return;

            // The result handlers saw is the one the click takes: it's never worked out again in between.
            var stale = !args.ArrivalState.IsCurrent(player)
                || !CraftingResultFollowsGrid(player);
            var outputCount = window is SmeltingContainer smelting ? smelting[2]?.Count ?? 0 : 0;

            try
            {
                // The client ends its drag on the correction, so the server's ends too.
                if (stale || args.IsCancelled)
                    ResetDrag(player);
                else
                    ApplyClick(args);

                if (!stale)
                {
                    foreach (var change in args.Changes)
                        change();
                }

                menuClick?.Commit();
            }
            catch when (menuClick is not null)
            {
                menuClick.Undo();
                throw;
            }

            if (window is SmeltingContainer furnace
                && (furnace[2]?.Count ?? 0) < outputCount
                && FurnaceBlockEntity.Of(furnace) is FurnaceBlockEntity machine)
            {
                cookingExperience = machine.TakeExperience(PlayerRecipes(player), Globals.Random.NextSingle);
            }

            // After the click and the changes registered with it, whatever they did to the grid, and before the
            // window is captured, so that its viewers see the result that follows.
            RefreshCraftingResult(player);

            sync = CaptureWindow(args, window);
        }

        if (cookingExperience > 0 && player.Level is AbstractLevel level)
            level.DropCookingExperience(player.Position, cookingExperience);

        await SendWindowAsync(args, window, sync);
    }

    // Whether the player still has the window open, under the click's id.
    private static bool IsOpen(IPlayer player, BaseContainer window, int containerId)
    {
        var openId = player.OpenedContainer is null ? 0 : player.CurrentContainerId;

        // A retired container's block entity is gone, or its plugin unloaded: its items moved on, so none move now.
        return (player.OpenedContainer ?? player.Inventory) == window
            && containerId == openId
            && !window.ShowsRetired;
    }

    private static void ApplyClick(ContainerClickEventArgs args)
    {
        // Before any window's own handling, so that crafting follows the same drag rules.
        if (CancelsDrag(args))
            return;

        if (HandleCrafting(args))
            return;

        HandleClick(args, dropThrown: true);
    }

    /// <summary>
    /// Handles a click in any window but a crafting grid's result slot, like vanilla's container menus.
    /// </summary>
    /// <param name="args">The click.</param>
    /// <param name="dropThrown">Whether items thrown from a slot drop in the world.</param>
    private static void HandleClick(ContainerClickEventArgs args, bool dropThrown)
    {
        if (CancelsDrag(args))
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

    /// <summary>
    /// Shift-clicks a slot like vanilla: the window's own routing moves the stack, again while it moves something and
    /// the slot still holds the same item. A click outside the window drops the cursor like a pickup.
    /// </summary>
    private static void HandleQuickMove(ContainerClickEventArgs args)
    {
        var player = args.Player;

        if (args.ClickedSlot == OutsideInventory)
        {
            if (args.Button is 0 or 1)
                SpawnThrownItem(player, ThrowItem(player, args.Container, args.ClickedSlot, args.Button, true));

            return;
        }

        if (args.ClickedSlot < 0 || !SlotMayPickup(player, args.Container, args.ClickedSlot))
            return;

        while (args.SlotItem is ItemStack before && !before.IsAir && QuickMoveOnce(args))
        {
            if (args.SlotItem is not ItemStack after || after.IsAir || after.Holder != before.Holder)
                return;
        }
    }

    /// <summary>
    /// Moves the clicked stack once, the way the open window routes it (vanilla's <c>quickMoveStack</c>).
    /// </summary>
    /// <returns>Whether anything moved and the routing goes on (see <see cref="MoveBrewingItem"/>).</returns>
    private static bool QuickMoveOnce(ContainerClickEventArgs args)
    {
        var player = args.Player;
        var inventory = player.Inventory;
        var source = args.Container;
        var slot = args.ClickedSlot;

        switch (player.OpenedContainer)
        {
            case SmeltingContainer furnace when IsFurnace(furnace):
                return MoveFurnaceItem(args, furnace);
            case CraftingTable { Type: InventoryType.Crafting } table:
                return MoveCraftingTableItem(player, table, source, slot);
            case EnchantmentTable table:
                return MoveEnchantingItem(player, table, source, slot);
            case BrewingStand stand:
                return MoveBrewingItem(args, stand);
            case MenuContainer menu:
                return MoveMenuItem(args, menu);
        }

        // Like vanilla's chest, hopper and dispenser menus: the container's stack goes to the player's main inventory
        // and hotbar, filling from the hotbar's last slot, and the player's goes to the container, filling from its
        // first slot.
        if (source != inventory)
            return MoveStack(player, source, slot, inventory, 9, 45, reverse: true);

        if (player.OpenedContainer is BaseContainer opened && opened != inventory)
            return MoveStack(player, inventory, slot, opened, 0, opened.Size, reverse: false);

        return MoveInventoryItem(player, slot);
    }

    /// <summary>
    /// Moves a stack within the player's own inventory window like vanilla's inventory menu: from the grid and armour
    /// to the main inventory and hotbar; armour and off-hand items to their empty slot; otherwise between the main
    /// inventory and the hotbar.
    /// </summary>
    private static bool MoveInventoryItem(IPlayer player, int slot)
    {
        var inventory = player.Inventory;
        var item = inventory.GetItem(slot);
        var armorSlot = CombatItems.PlayerArmorSlot(item);

        if (slot is >= 1 and <= 8)
            return MoveStack(player, inventory, slot, inventory, 9, 45, reverse: false);

        if (armorSlot >= 5 && inventory.GetItem(armorSlot).IsNullOrAir())
            return MoveStack(player, inventory, slot, inventory, armorSlot, armorSlot + 1, reverse: false);

        if (CombatItems.IsOffhandItem(item)
            && OffhandSlot < inventory.Size
            && inventory.GetItem(OffhandSlot).IsNullOrAir())
        {
            return MoveStack(player, inventory, slot, inventory, OffhandSlot, OffhandSlot + 1, reverse: false);
        }

        return slot switch
        {
            >= 9 and < 36 => MoveStack(player, inventory, slot, inventory, 36, 45, reverse: false),
            >= 36 and < 45 => MoveStack(player, inventory, slot, inventory, 9, 36, reverse: false),
            _ => MoveStack(player, inventory, slot, inventory, 9, 45, reverse: false)
        };
    }

    /// <summary>
    /// Moves the stack in <paramref name="fromSlot"/> into slots <paramref name="start"/> (inclusive) to
    /// <paramref name="end"/> (exclusive) of <paramref name="to"/>. What doesn't fit stays.
    /// </summary>
    /// <returns>Whether anything moved.</returns>
    private static bool MoveStack(
        IPlayer player,
        BaseContainer from,
        int fromSlot,
        BaseContainer to,
        int start,
        int end,
        bool reverse)
    {
        var stack = from.GetItem(fromSlot);
        if (stack.IsNullOrAir())
            return false;

        var moved = MoveItemStackTo(player, stack, to, start, end, reverse);

        from.SetItem(fromSlot, stack.Count > 0 ? stack : null);

        return moved;
    }

    /// <summary>
    /// Moves what fits of <paramref name="stack"/>, which shrinks, into slots <paramref name="start"/> (inclusive) to
    /// <paramref name="end"/> (exclusive) of <paramref name="to"/>, like vanilla's <c>moveItemStackTo</c>: it tops up
    /// the stacks of the same item first, then fills the first empty slot that takes it, walking the range backwards
    /// when <paramref name="reverse"/>. The slots' rules and limits apply (see <see cref="SlotAccepts"/>).
    /// </summary>
    /// <returns>Whether anything moved.</returns>
    private static bool MoveItemStackTo(
        IPlayer player,
        ItemStack stack,
        BaseContainer to,
        int start,
        int end,
        bool reverse)
    {
        var slots = Enumerable.Range(start, Math.Max(0, Math.Min(end, to.Size) - start));
        if (reverse)
            slots = slots.Reverse();

        return MoveItemStackTo(player, stack, [.. slots.Select(slot => (to, slot))]);
    }

    /// <summary>
    /// Moves what fits of <paramref name="stack"/>, which shrinks, into <paramref name="slots"/>, in their order, like
    /// vanilla's <c>moveItemStackTo</c> (see the overload taking one container's range).
    /// </summary>
    /// <returns>Whether anything moved.</returns>
    private static bool MoveItemStackTo(IPlayer player, ItemStack stack, (BaseContainer To, int Slot)[] slots)
    {
        var moved = false;

        if (stack.MaxStackSize > 1)
        {
            foreach (var (to, slot) in slots)
            {
                if (stack.Count == 0)
                    break;

                var target = to.GetItem(slot);
                if (target.IsNullOrAir() || target != stack)
                    continue;

                var limit = SlotLimit(player, to, slot, target);
                var added = Math.Min(stack.Count, limit - target.Count);
                if (added <= 0)
                    continue;

                target.Count += added;
                stack.Count -= added;
                to.SetItem(slot, target);
                moved = true;
            }
        }

        if (stack.Count == 0)
            return moved;

        // Vanilla's merge fills one empty slot, then stops.
        foreach (var (to, slot) in slots)
        {
            if (!to.GetItem(slot).IsNullOrAir() || !SlotAccepts(player, to, slot, stack))
                continue;

            var count = Math.Min(stack.Count, SlotLimit(player, to, slot, stack));
            to.SetItem(slot, new ItemStack(stack, count));
            stack.Count -= count;

            return true;
        }

        return moved;
    }

    private static void HandleClone(ContainerClickEventArgs args)
    {
        var player = args.Player;
        var clickedItem = args.SlotItem;

        if (player.GameMode != GameMode.Creative || !player.CarriedItem.IsNullOrAir() || clickedItem.IsNullOrAir())
            return;

        player.CarriedItem = new ItemStack(clickedItem, clickedItem.MaxStackSize);
    }

    /// <summary>
    /// Double-click collecting, like vanilla's: with a stack on the cursor and the clicked slot empty (or one it can't
    /// be taken from), the cursor fills up from the window's slots of the same item, partial stacks first, then full
    /// ones. The left button walks the window from its first slot, the right one from its last; result slots of
    /// crafting grids are skipped.
    /// </summary>
    private static void HandlePickupAll(ContainerClickEventArgs args)
    {
        var player = args.Player;
        var carried = player.CarriedItem;

        if (args.ClickedSlot < 0 || carried.IsNullOrAir())
            return;

        if (!args.SlotItem.IsNullOrAir() && SlotMayPickup(player, args.Container, args.ClickedSlot))
            return;

        var slots = WindowSlots(player);
        var step = args.Button == 0 ? 1 : -1;
        var first = args.Button == 0 ? 0 : slots.Length - 1;

        for (var pass = 0; pass < 2; pass++)
        {
            for (var index = first; index >= 0 && index < slots.Length; index += step)
            {
                if (carried.Count >= carried.MaxStackSize)
                    return;

                var (container, slot) = slots[index];
                var item = container.GetItem(slot);

                if (item.IsNullOrAir()
                    || item != carried
                    || pass == 0 && item.Count == item.MaxStackSize
                    || IsResultSlot(player, container, slot)
                    || !SlotMayPickup(player, container, slot))
                {
                    continue;
                }

                var room = carried.MaxStackSize - carried.Count;

                // Like vanilla, a slot that takes nothing (a furnace's result) gives its stack only whole.
                if (!SlotAccepts(player, container, slot, item) && room < item.Count)
                    continue;

                var taken = Math.Min(item.Count, room);
                container.RemoveItem(slot, taken);
                carried.Count += taken;
            }
        }
    }

    /// <summary>
    /// The open window's slots, in the order the window numbers them: the open container's slots then the player's
    /// main inventory and hotbar, or the player's whole inventory when no container is open.
    /// </summary>
    private static (BaseContainer Container, int Slot)[] WindowSlots(IPlayer player)
    {
        var inventory = player.Inventory;

        if (player.OpenedContainer is not BaseContainer window || window == inventory)
            return [.. Enumerable.Range(0, inventory.Size).Select(slot => ((BaseContainer)inventory, slot))];

        var own = Enumerable.Range(0, window.Size).Select(slot => (window, slot));
        var players = Enumerable.Range(9, 36).Select(slot => ((BaseContainer)inventory, slot));

        return [.. own.Concat(players)];
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

        EndDrag(player, carried);
    }

    private static void EndDrag(IPlayer player, ItemStack carried)
    {
        var start = player.DragStart;
        var slots = player.DraggedSlots.ToArray();

        ResetDrag(player);

        if (slots.Length == 1)
        {
            // Like vanilla, a drag over one slot is a click on it, which keeps the slot's rules and limits and places
            // nothing for full stacks.
            if (start != DraggingState.StartMiddle
                && TryGetDragTarget(player, slots[0], carried, out var container, out var slot))
            {
                PickupSlot(player, container, slot, primary: start == DraggingState.StartLeft);
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
            var count = Math.Min(share + existing, SlotLimit(player, container, slot, carried));

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
    /// dragged onto it: the slot takes the item (see <see cref="SlotAccepts"/>) and is empty or holds the same item.
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
        if (windowSlot < 0
            || slot < 0
            || slot >= container.Size
            || forPlayer && slot is < 9 or >= 45
            || !SlotAccepts(player, container, slot, carried))
        {
            return false;
        }

        var target = container.GetItem(slot);

        return target.IsNullOrAir() || target == carried;
    }

    /// <summary>
    /// Whether the player can put <paramref name="item"/> in <paramref name="slot"/> of <paramref name="container"/>,
    /// like vanilla's slots allow: not in a result slot, armour only in its own slot, lapis lazuli only in the
    /// enchanting table's second slot, only fuel (or a bucket) in a furnace's fuel slot, and only what each of a
    /// brewing stand's slots takes (see <see cref="Brewing.SlotAccepts"/>); and what a plugin's menu says in its
    /// slots.
    /// </summary>
    private static bool SlotAccepts(IPlayer player, BaseContainer container, int slot, ItemStack item)
    {
        if (container == player.Inventory)
        {
            return slot switch
            {
                0 => false,
                >= 5 and <= 8 => CombatItems.PlayerArmorSlot(item) == slot,
                _ => true
            };
        }

        return container switch
        {
            CraftingTable { Type: InventoryType.Crafting } => slot != 0,
            EnchantmentTable => slot != 1 || item.Is(ItemsRegistry.LapisLazuli),
            BrewingStand => Brewing.SlotAccepts(slot, item),
            MenuContainer menu => menu.MayPlace(slot, item),
            SmeltingContainer furnace when IsFurnace(furnace) => slot switch
            {
                1 => IsFuel(item) || item.Is(ItemsRegistry.Bucket),
                2 => false,
                _ => true
            },
            _ => true
        };
    }

    /// <summary>
    /// How many of <paramref name="item"/> <paramref name="slot"/> of <paramref name="container"/> holds, like
    /// vanilla's slots: one in an armour slot, in the enchanting table's first slot, in a brewing stand's bottle slots
    /// and, for a bucket, in a furnace's fuel slot; what a plugin's menu says in its slots.
    /// </summary>
    private static int SlotLimit(IPlayer player, BaseContainer container, int slot, ItemStack item)
    {
        if (container is MenuContainer menu)
            return menu.MaxStackSize(slot, item);

        var single = container == player.Inventory && slot is >= 5 and <= 8
            || container is EnchantmentTable && slot == 0
            || container is BrewingStand && slot < BrewingStand.IngredientSlot
            || container is SmeltingContainer furnace
                && IsFurnace(furnace)
                && slot == 1
                && item.Is(ItemsRegistry.Bucket);

        return single ? 1 : item.MaxStackSize;
    }

    /// <summary>
    /// Whether the player can take the stack in <paramref name="slot"/> of <paramref name="container"/>: like
    /// vanilla's armour slots, not armour cursed with binding, unless the player is in creative mode; and what a
    /// plugin's menu says in its slots.
    /// </summary>
    private static bool SlotMayPickup(IPlayer player, BaseContainer container, int slot)
    {
        if (container is MenuContainer menu)
            return menu.MayPickup(slot, player);

        return container != player.Inventory
            || slot is < 5 or > 8
            || player.GameMode == GameMode.Creative
            || !player.Alive
            || CombatItems.EnchantmentLevel(container.GetItem(slot), EnchantmentsRegistry.BindingCurse) == 0;
    }

    // Whether the slot is a crafting grid's result, which double-click collecting skips.
    private static bool IsResultSlot(IPlayer player, BaseContainer container, int slot) =>
        slot == 0 && (container == player.Inventory || container is CraftingTable { Type: InventoryType.Crafting });

    /// <summary>
    /// Cancels the player's drag if <paramref name="args"/> isn't part of it: like vanilla, any other click while
    /// dragging only cancels the drag.
    /// </summary>
    /// <returns>Whether the click cancelled a drag, and does nothing else.</returns>
    private static bool CancelsDrag(ContainerClickEventArgs args)
    {
        if (args.ClickType == ClickType.QuickCraft || !args.Player.IsDragging)
            return false;

        ResetDrag(args.Player);

        return true;
    }

    private static void ResetDrag(IPlayer player)
    {
        player.IsDragging = false;
        player.DraggedSlots.Clear();
    }

    // Like vanilla, the Q key throws from a slot only with an empty cursor, and not what the slot keeps.
    private static void HandleThrow(ContainerClickEventArgs args, bool test = false)
    {
        var clickedSlot = args.ClickedSlot;
        var container = args.Container;
        var player = args.Player;

        if (clickedSlot < 0
            || !player.CarriedItem.IsNullOrAir()
            || !SlotMayPickup(player, container, clickedSlot))
        {
            return;
        }

        var thrownItem = ThrowItem(player, container, clickedSlot, args.Button);

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

        if (MenuClick.Current is MenuClick click)
            click.Drop(thrownItem);
        else
            ItemEntity.Drop(player, thrownItem);
    }

    private static void HandlePickup(ContainerClickEventArgs args)
    {
        var player = args.Player;

        if (args.Button is not (0 or 1))
            return;

        if (args.ClickedSlot == OutsideInventory)
        {
            SpawnThrownItem(player, ThrowItem(player, args.Container, args.ClickedSlot, args.Button, true));
            return;
        }

        if (args.ClickedSlot >= 0)
            PickupSlot(player, args.Container, args.ClickedSlot, primary: args.Button == 0);
    }

    /// <summary>
    /// Clicks a slot like vanilla's pickup, for every window and slot but a crafting grid's result, keeping the slot's
    /// rules and limits (see <see cref="SlotAccepts"/>, <see cref="SlotLimit"/> and <see cref="SlotMayPickup"/>).
    /// </summary>
    /// <remarks>
    /// With an empty cursor, the left button takes the stack and the right one half of it, rounded up. With a stack
    /// on the cursor, an empty slot or one of the same item takes all of it (left) or one (right), up to its limit;
    /// a slot of another item swaps with the cursor if it takes the cursor's stack whole. A slot that takes nothing
    /// gives its stack to a cursor of the same item if it fits whole.
    /// </remarks>
    private static void PickupSlot(IPlayer player, BaseContainer container, int slot, bool primary)
    {
        var item = container.GetItem(slot);
        var carried = player.CarriedItem;

        if (item.IsNullOrAir())
        {
            if (!carried.IsNullOrAir())
                InsertCarried(player, container, slot, primary ? carried.Count : 1);

            return;
        }

        if (!SlotMayPickup(player, container, slot))
            return;

        if (carried.IsNullOrAir())
        {
            var count = primary ? item.Count : (item.Count + 1) / 2;
            player.CarriedItem = new ItemStack(item, count);
            container.RemoveItem(slot, count);
        }
        else if (SlotAccepts(player, container, slot, carried))
        {
            if (item == carried)
                InsertCarried(player, container, slot, primary ? carried.Count : 1);
            else if (carried.Count <= SlotLimit(player, container, slot, carried))
            {
                container.SetItem(slot, carried);
                player.CarriedItem = item;
            }
        }
        else if (item == carried && item.Count <= carried.MaxStackSize - carried.Count)
        {
            carried.Count += item.Count;
            container.RemoveItem(slot);
        }
    }

    /// <summary>
    /// Puts up to <paramref name="count"/> of the cursor's stack in <paramref name="slot"/>, which is empty or holds
    /// the same item, like vanilla's <c>safeInsert</c>.
    /// </summary>
    private static void InsertCarried(IPlayer player, BaseContainer container, int slot, int count)
    {
        var carried = player.CarriedItem!;
        if (!SlotAccepts(player, container, slot, carried))
            return;

        var target = container.GetItem(slot);
        var existing = target.IsNullOrAir() ? 0 : target.Count;
        var moved = Math.Min(Math.Min(count, carried.Count), SlotLimit(player, container, slot, carried) - existing);

        if (moved <= 0 || existing > 0 && target != carried)
            return;

        container.SetItem(slot, new ItemStack(carried, existing + moved));
        carried.Count -= moved;

        if (carried.Count == 0)
            player.CarriedItem = null;
    }

    /// <summary>
    /// Swaps a slot with a hotbar slot (buttons 0 to 8) or the off hand (40), like vanilla's swap: a slot gives its
    /// stack only if it may be taken from, and takes the other stack only if it accepts it, up to its limit. When the
    /// slot holds less than the other stack, the rest stays; when that happens with a stack on both sides, the slot's
    /// stack goes to the player's inventory like a picked-up item (see <see cref="Player.AddToInventory"/>), and what
    /// doesn't fit drops.
    /// </summary>
    private static void HandleSwap(ContainerClickEventArgs args)
    {
        var player = args.Player;
        var container = args.Container;
        var slot = args.ClickedSlot;
        var inventory = player.Inventory;

        if (slot < 0 || args.Button is not (>= 0 and <= 8 or 40))
            return;

        var sourceSlot = args.Button == 40 ? OffhandSlot : args.Button + 36;
        if (sourceSlot >= inventory.Size)
            return;

        var source = inventory.GetItem(sourceSlot);
        var item = container.GetItem(slot);

        if (source.IsNullOrAir())
        {
            if (item.IsNullOrAir() || !SlotMayPickup(player, container, slot))
                return;

            container.RemoveItem(slot);
            inventory.SetItem(sourceSlot, item);

            return;
        }

        if (!SlotAccepts(player, container, slot, source))
            return;

        var limit = SlotLimit(player, container, slot, source);

        if (item.IsNullOrAir())
        {
            if (source.Count > limit)
            {
                container.SetItem(slot, new ItemStack(source, limit));
                source.Count -= limit;
                inventory.SetItem(sourceSlot, source);
            }
            else
            {
                inventory.RemoveItem(sourceSlot);
                container.SetItem(slot, source);
            }

            return;
        }

        if (!SlotMayPickup(player, container, slot))
            return;

        if (source.Count > limit)
        {
            container.SetItem(slot, new ItemStack(source, limit));
            source.Count -= limit;
            inventory.SetItem(sourceSlot, source);

            Player.AddToInventory(inventory, player.CurrentHeldItemSlot, item, player.GameMode == GameMode.Creative);
            SpawnThrownItem(player, item);
        }
        else
        {
            inventory.SetItem(sourceSlot, item);
            container.SetItem(slot, source);
        }
    }

    /// <summary>
    /// Captures, inside the window's lock, what the click's window shows its viewers (see <see cref="WindowPackets"/>):
    /// the clicking player and the other players viewing the same slots, so that it can be sent after the lock.
    /// </summary>
    private static WindowSync CaptureWindow(ContainerClickEventArgs args, BaseContainer window)
    {
        var sync = new WindowSync();

        foreach (var viewer in WindowViewers(args.Player, window))
        {
            // Taken before the contents, so that a change made while they're read leaves it behind.
            var state = ClickWindowState.Of(viewer);

            sync.Add(viewer, state, WindowPackets(args, viewer));
        }

        return sync;
    }

    /// <summary>
    /// Sends a click's captured window to its viewers, leaving out what's obsolete (see <see cref="WindowSnapshots"/>).
    /// When a viewer's window changed before its contents went through plugins' packet handlers, the window is
    /// captured again, a few times at most; after that, a window still changing is left stale, for the viewer's tick
    /// to send (see <see cref="RefreshStaleWindowAsync"/>).
    /// </summary>
    private static async ValueTask SendWindowAsync(ContainerClickEventArgs args, BaseContainer window, WindowSync sync)
    {
        for (var sends = 1; sends < MaxWindowSends; sends++)
        {
            if (!await sync.SendAsync())
                return;

            using (await BaseContainer.LockWindowAsync(window, args.Player.Inventory))
            {
                if (!IsOpen(args.Player, window, args.ContainerId))
                    return;

                sync = CaptureWindow(args, window);
            }
        }

        await sync.SendAsync();
    }

    /// <summary>
    /// Sends <paramref name="player"/> their whole window, through plugins' packet handlers, if something about it
    /// wasn't sent because it changed first (see <see cref="WindowSnapshots.IsStale"/>). The player's tick calls it,
    /// once a tick: a window that changed again meanwhile stays stale until a later tick.
    /// </summary>
    internal static async ValueTask RefreshStaleWindowAsync(Player player)
    {
        if (!player.WindowSnapshots.IsStale)
            return;

        // A merchant's clicks are applied only with the state id its client last got, so its window is sent in turn
        // with them, with that id.
        if (player.OpenedContainer is MerchantContainer merchant)
        {
            await merchant.SendInTurnAsync(stateId => RefreshWindowAsync(player, merchant, stateId));
            return;
        }

        // Other windows' clicks are checked against what they saw as they arrived, not against the state id, which
        // is 0 here like in the snapshots a click sends the window's other viewers.
        await RefreshWindowAsync(player, player.OpenedContainer ?? player.Inventory, stateId: 0);
    }

    /// <summary>
    /// Sends the players who have <paramref name="container"/> open their whole window, after the server changed its
    /// slots outside a click, like a brewing stand finishing a brew. Don't hold the container's lock: this takes it.
    /// </summary>
    internal static async ValueTask RefreshViewersAsync(BaseContainer container)
    {
        foreach (var viewer in container.GetViewers())
        {
            if (viewer is Player player
                && player.OpenedContainer is BaseContainer shown
                && shown.SharesSlotsWith(container))
            {
                await RefreshWindowAsync(player, shown, stateId: 0);
            }
        }
    }

    // Captures the player's whole window, inside its lock, under the given state id, and sends it unless it changed
    // or another window opened meanwhile.
    internal static async ValueTask RefreshWindowAsync(Player player, BaseContainer window, int stateId)
    {
        var order = player.WindowSnapshots;
        WindowSnapshot snapshot;

        using (await BaseContainer.LockWindowAsync(window, player.Inventory))
        {
            // Another window opened while the lock was taken: a later tick sends it.
            if ((player.OpenedContainer ?? player.Inventory) != window)
                return;

            var state = ClickWindowState.Of(player);
            var containerId = player.OpenedContainer is null ? 0 : player.CurrentContainerId;
            var packets = WindowPackets(player, containerId, stateId, withEnchanting: true);

            snapshot = new WindowSnapshot(player, order.Next(), state, packets);
        }

        await order.SendAsync(snapshot);
    }

    // The players a click's window is sent to: the clicking player, then the others viewing the same slots.
    private static IEnumerable<IPlayer> WindowViewers(IPlayer player, BaseContainer window)
    {
        yield return player;

        foreach (var viewer in window.GetViewers())
        {
            if (viewer != player
                && viewer.OpenedContainer is BaseContainer shown
                && shown.SharesSlotsWith(window))
            {
                yield return viewer;
            }
        }
    }

    /// <summary>
    /// What <paramref name="viewer"/>'s window shows after the click: its contents and their cursor, the enchanting
    /// options of an enchanting table the clicking player has open, and the off hand of a viewer with a container open.
    /// </summary>
    /// <remarks>
    /// A container's window doesn't show the off hand, so it's set in the player's inventory, which vanilla's client
    /// numbers like its saved inventory. It's sent with every snapshot of such a window, not only after a swap with
    /// the off hand, so that it's corrected even when that click's snapshot is left out for a newer one.
    /// </remarks>
    private static List<IClientboundPacket> WindowPackets(ContainerClickEventArgs args, IPlayer viewer) =>
        viewer == args.Player
            ? WindowPackets(viewer, args.ContainerId, args.StateId + 1, withEnchanting: true)
            : WindowPackets(viewer, viewer.CurrentContainerId, stateId: 0, withEnchanting: false);

    // What the viewer's window shows, under the given id and state id, with the enchanting options of an enchanting
    // table they have open when asked for.
    private static List<IClientboundPacket> WindowPackets(
        IPlayer viewer,
        int containerId,
        int stateId,
        bool withEnchanting)
    {
        var packets = new List<IClientboundPacket>
        {
            WindowContents(viewer, containerId, stateId)
        };

        if (withEnchanting && viewer is Player { OpenedContainer: EnchantmentTable } enchanter)
            packets.AddRange(enchanter.PrepareEnchantingProperties());

        if (viewer.OpenedContainer is not null)
        {
            packets.Add(new SetPlayerInventoryPacket
            {
                Slot = OffhandInventorySlot,
                Contents = Copy(viewer.Inventory.GetItem(OffhandSlot))
            });
        }

        return packets;
    }

    /// <summary>
    /// What <paramref name="player"/>'s open window shows, copied: the open container's slots then their main
    /// inventory and hotbar, or their whole inventory; and their cursor.
    /// </summary>
    private static ContainerSetContentPacket WindowContents(IPlayer player, int containerId, int stateId)
    {
        IEnumerable<ItemStack> shown = player.OpenedContainer is BaseContainer window
            ? window.Concat(player.Inventory.Skip(9).Take(36))
            : player.Inventory;

        return new ContainerSetContentPacket(containerId, [.. shown.Select(item => Copy(item)!)])
        {
            StateId = stateId,
            CarriedItem = Copy(player.CarriedItem)
        };
    }

    // A copy of a stack to send, which later changes to the stack, its components included, don't reach.
    private static ItemStack? Copy(ItemStack? item) => item is null ? null : NetworkBuffer.SnapshotItemStack(item);

    /// <summary>
    /// The packets a click's window is sent with, captured inside the window's lock: a snapshot for each viewer.
    /// </summary>
    private sealed class WindowSync
    {
        private readonly List<WindowSnapshot> snapshots = [];

        // Numbers the viewer's snapshot as it's captured, so that it's never sent after a newer one.
        public void Add(IPlayer viewer, ClickWindowState state, IReadOnlyList<IClientboundPacket> packets)
        {
            var sequence = viewer is Player { WindowSnapshots: WindowSnapshots order } ? order.Next() : 0;

            this.snapshots.Add(new WindowSnapshot(viewer, sequence, state, packets));
        }

        /// <summary>
        /// Sends each viewer's snapshot unless it's obsolete.
        /// </summary>
        /// <returns>Whether a viewer's window changed since its snapshot's capture, so that it wasn't sent.</returns>
        public async ValueTask<bool> SendAsync()
        {
            var outdated = false;

            foreach (var snapshot in this.snapshots)
            {
                if (snapshot.Player is Player { WindowSnapshots: WindowSnapshots order })
                {
                    outdated |= await order.SendAsync(snapshot);
                    continue;
                }

                foreach (var packet in snapshot.Packets)
                    await snapshot.Player.QueuePacketAsync(packet);
            }

            return outdated;
        }
    }

    internal static void OnInventoryClickTest(ContainerClickEventArgs args) => HandleClick(args, dropThrown: false);
}
