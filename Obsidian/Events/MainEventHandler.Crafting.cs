using Obsidian.API.Crafting;
using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.API.Inventory;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Diagnostics;

namespace Obsidian.Events;

public partial class MainEventHandler
{
    internal static RecipeSet PlayerRecipes(IPlayer player) => player is Player { Server: Server server }
        ? server.RecipeRegistry.Current
        : RecipesRegistry.Current;

    /// <summary>
    /// Handles a click in a window with a crafting grid, the player's inventory or a crafting table: its result slot
    /// crafts, the others work like any window's. The result shown is the one taken, as the click is applied only if
    /// it follows the grid (see <see cref="CraftingResultFollowsGrid"/>); it's refreshed after the click.
    /// </summary>
    /// <returns>Whether the window has a crafting grid, so that the click is handled.</returns>
    private static bool HandleCrafting(ContainerClickEventArgs args)
    {
        if (!TryGetCraftingGrid(args.Player, out var grid, out var width))
            return false;

        // Drags follow the same rules as in any window, the slots' limits included; the result slot takes none.
        if (args.Container == grid && args.ClickedSlot == 0 && args.ClickType != ClickType.QuickCraft)
            TakeCraftingResult(args, grid, width);
        else
            HandleClick(args, dropThrown: true);

        return true;
    }

    /// <summary>
    /// Finds the crafting grid the player's open window shows: their inventory's 2x2 one, or a crafting table's.
    /// </summary>
    private static bool TryGetCraftingGrid(IPlayer player, out BaseContainer grid, out int width)
    {
        switch (player.OpenedContainer)
        {
            case null:
                grid = player.Inventory;
                width = 2;

                return true;
            case CraftingTable { Type: InventoryType.Crafting } table:
                grid = table;
                width = 3;

                return true;
            default:
                grid = player.Inventory;
                width = 0;

                return false;
        }
    }

    /// <summary>
    /// Whether the result slot of the player's open crafting grid, if they have one open, shows what its ingredients
    /// craft now. It doesn't when something changed the grid without refreshing it, or the recipes changed.
    /// </summary>
    private static bool CraftingResultFollowsGrid(IPlayer player) =>
        !TryGetCraftingGrid(player, out var grid, out var width)
        || ShowsResult(grid, CraftingResult(player, grid, width));

    /// <summary>
    /// Sets the result slot of the player's open crafting grid, if they have one open, to what its ingredients craft.
    /// </summary>
    private static void RefreshCraftingResult(IPlayer player)
    {
        if (TryGetCraftingGrid(player, out var grid, out var width))
            RefreshCraftingResult(player, grid, width);
    }

    /// <summary>
    /// Sets the result slot of the player's open crafting grid to what its ingredients craft with the current recipes,
    /// and sends the window to its viewers. Sending it even when unchanged lets a failed delivery catch up on the
    /// next recipe change (a plugin's recipes registered or removed), so no grid keeps showing a result whose recipe
    /// is gone, or misses one just added.
    /// </summary>
    internal static async ValueTask RefreshCraftingResultAsync(Player player)
    {
        var window = player.OpenedContainer ?? player.Inventory;

        using (await BaseContainer.LockWindowAsync(window, player.Inventory))
        {
            // Another window opened while the lock was taken: it shows its own result already.
            if ((player.OpenedContainer ?? player.Inventory) != window
                || !TryGetCraftingGrid(player, out var grid, out var width))
            {
                return;
            }

            RefreshCraftingResult(player, grid, width);
        }

        foreach (var viewer in WindowViewers(player, window))
        {
            if (viewer is Player shown)
                await RefreshWindowAsync(shown, shown.OpenedContainer ?? shown.Inventory, stateId: 0);
        }
    }

    private static void RefreshCraftingResult(IPlayer player, BaseContainer grid, int width)
    {
        var result = CraftingResult(player, grid, width);

        // Setting the same result again would count as a change to the grid (see BaseContainer.Revision).
        if (!ShowsResult(grid, result))
            grid.SetItem(0, result is null ? null : NetworkBuffer.SnapshotItemStack(result));
    }

    // What the grid's ingredients craft, as the recipe holds it: copy it to put it in a slot.
    private static ItemStack? CraftingResult(IPlayer player, BaseContainer grid, int width)
    {
        var recipes = PlayerRecipes(player);
        if (!RecipeBookSync.CanSend(player, recipes))
            recipes = RecipesRegistry.Current;

        var result = recipes.FindRecipe(grid, width)?.Result.FirstOrDefault();
        Debug.Assert(result is null || (result.Count > 0 && result.Count <= result.MaxStackSize),
            "Generated crafting results must fit in one stack.");

        return result;
    }

    // Whether the grid's result slot holds the result.
    private static bool ShowsResult(BaseContainer grid, ItemStack? result)
    {
        var current = grid.GetItem(0);

        return result is null
            ? current is null
            : current is not null && current == result && current.Count == result.Count;
    }

    /// <summary>
    /// Takes the crafting result like vanilla's result slot, which gives its whole stack and takes nothing: a pickup
    /// moves it to the cursor (or onto a cursor of the same item, if it fits whole), a swap to an empty hotbar slot or
    /// the off hand, a throw drops it, and each crafts once. A shift-click crafts again while the result stays the
    /// same item and some of it fits in the player's inventory, filling from the hotbar's last slot and dropping
    /// what doesn't fit; throwing with the right button crafts again too.
    /// </summary>
    private static void TakeCraftingResult(ContainerClickEventArgs args, BaseContainer grid, int width)
    {
        var player = args.Player;
        if (args.ClickType == ClickType.Clone)
        {
            HandleClone(args);
            return;
        }

        var repeats = args.ClickType == ClickType.QuickMove || args.ClickType == ClickType.Throw && args.Button == 1;
        var crafted = grid.GetItem(0)?.Holder;

        while (grid.GetItem(0) is ItemStack result && !result.IsAir && result.Holder == crafted)
        {
            var stack = NetworkBuffer.SnapshotItemStack(result);

            switch (args.ClickType)
            {
                case ClickType.Pickup when args.Button is 0 or 1:
                    var carried = player.CarriedItem;
                    if (carried.IsNullOrAir())
                        player.CarriedItem = stack;
                    else if (carried == stack && carried.Count + stack.Count <= carried.MaxStackSize)
                        carried.Count += stack.Count;
                    else
                        return;

                    break;
                case ClickType.QuickMove:
                    if (!MoveItemStackTo(player, stack, player.Inventory, 9, 45, reverse: true))
                        return;

                    break;
                case ClickType.Swap when args.Button is >= 0 and <= 8 or 40:
                    var slot = args.Button == 40 ? OffhandSlot : args.Button + 36;
                    if (!player.Inventory.GetItem(slot).IsNullOrAir())
                        return;

                    player.Inventory.SetItem(slot, stack);
                    break;
                case ClickType.Throw when args.Button is 0 or 1 && player.CarriedItem.IsNullOrAir():
                    SpawnThrownItem(player, stack);
                    break;
                default:
                    return;
            }

            if (player is Player progressing
                && PlayerRecipes(player).FindRecipe(grid, width) is IRecipeWithResult recipe)
                progressing.RecordProgressCraft(recipe.Identifier, result);

            ConsumeCraftingIngredients(player, grid, width);

            // Like vanilla's shift-click, what didn't fit drops.
            SpawnThrownItem(player, stack.Count > 0 && args.ClickType == ClickType.QuickMove ? stack : null);
            RefreshCraftingResult(player, grid, width);

            if (!repeats)
                return;
        }
    }

    private static void ConsumeCraftingIngredients(IPlayer player, BaseContainer grid, int width)
    {
        for (int slot = 1; slot <= width * width; slot++)
        {
            var ingredient = grid.GetItem(slot);
            if (ingredient.IsNullOrAir() || ingredient.Count <= 0)
                continue;

            var remainder = ingredient.Holder.Id switch
            {
                ItemIds.MilkBucket or ItemIds.WaterBucket or ItemIds.LavaBucket
                    => ItemsRegistry.GetSingleItem(ItemsRegistry.Bucket),
                ItemIds.HoneyBottle => ItemsRegistry.GetSingleItem(ItemsRegistry.GlassBottle),
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

    /// <summary>
    /// Shift-clicks a stack in a crafting table's window like vanilla's crafting menu: from the player's inventory to
    /// the grid, else between the main inventory and the hotbar; from the grid to the player's inventory.
    /// </summary>
    private static bool MoveCraftingTableItem(IPlayer player, CraftingTable table, BaseContainer source, int slot)
    {
        var inventory = player.Inventory;

        if (source != inventory)
            return MoveStack(player, table, slot, inventory, 9, 45, reverse: false);

        if (MoveStack(player, inventory, slot, table, 1, 10, reverse: false))
            return true;

        return slot < 36
            ? MoveStack(player, inventory, slot, inventory, 36, 45, reverse: false)
            : MoveStack(player, inventory, slot, inventory, 9, 36, reverse: false);
    }

    /// <summary>
    /// Adds <paramref name="item"/>, which shrinks, to slots <paramref name="start"/> to <paramref name="end"/> of
    /// <paramref name="container"/>: stacks of the same item first, then empty slots.
    /// </summary>
    private static void StoreCraftingItem(BaseContainer container, ItemStack item, int start, int end)
    {
        for (int pass = 0; pass < 2 && item.Count > 0; pass++)
        {
            for (int slot = start; slot < end && item.Count > 0; slot++)
            {
                var target = container.GetItem(slot);
                if (pass == 0 ? target.IsNullOrAir() || target != item : !target.IsNullOrAir())
                    continue;

                var room = target.IsNullOrAir() ? item.MaxStackSize : target.MaxStackSize - target.Count;
                int amount = Math.Min(item.Count, room);
                if (amount <= 0)
                    continue;

                container.SetItem(slot, new ItemStack(item, (target?.Count ?? 0) + amount));
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
        await player.QueuePacketAsync(new ContainerSetContentPacket(0, player.Inventory.ToList()));
    }
}
