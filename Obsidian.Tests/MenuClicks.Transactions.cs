using Obsidian.API;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using PlayerEntity = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>
/// Clicks that move items between slots, the cursor and the ground, played through the click packet. Each checks
/// that no item appears or disappears, and the outcome that matters to the player rather than vanilla's exact routing.
/// </summary>
public sealed partial class MenuClicks
{
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ShiftCraftingTakesInputsPerCraftAndKeepsEveryOutput()
    {
        ShiftCraft[] cases =
        [
            // Two logs craft eight planks: six fill the last stack, two are dropped, and the third log stays.
            new("a nearly full inventory", FillForPlanks, new ItemStack(ItemsRegistry.OakPlanks, 1), Stored: 6,
                Dropped: 2, Inputs: [(1, 1)]),
            // Each map and blank map clone into two maps, three times; two blank maps alone craft nothing.
            new("a special recipe", FillForMaps, new ItemStack(ItemsRegistry.FilledMap, 1, MapSeven), Stored: 6,
                Dropped: 0, Inputs: [(1, 0), (2, 2)])
        ];

        await FixtureCases.AssertAllAsync(cases, craft => craft.Name, AssertShiftCraft);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ClicksConserveItems()
    {
        Transfer[] cases =
        [
            new("a drag ended without a start", CarryDirt,
                Clicks((1, 1, ClickType.QuickCraft), (2, 1, ClickType.QuickCraft), (-999, 2, ClickType.QuickCraft)),
                player => AssertCarries(player, ItemsRegistry.Dirt, 4)),
            new("a drag end alone", CarryDirt, Clicks((-999, 2, ClickType.QuickCraft)),
                player => AssertCarries(player, ItemsRegistry.Dirt, 4)),
            new("a drag over slots outside the window", CarryDirt,
                Clicks((-999, 0, ClickType.QuickCraft), (99, 1, ClickType.QuickCraft), (-999, 2, ClickType.QuickCraft)),
                player => AssertCarries(player, ItemsRegistry.Dirt, 4)),
            new("a number key into a one-bucket fuel slot", FillBucketSwap, Clicks((1, 0, ClickType.Swap)),
                player => AssertWindowSlot(player, 1, ItemsRegistry.Bucket, 1)),
            new("a number key displacing fuel", FillDisplacedFuel, Clicks((1, 0, ClickType.Swap)),
                player => AssertWindowSlot(player, 1, ItemsRegistry.Bucket, 1)),
            new("a double-click collecting stacks", FillDoubleClick, Clicks((2, 0, ClickType.PickupAll)),
                player => AssertCarries(player, ItemsRegistry.Stone, 64)),
            new("a furnace's output", FillFurnaceOutput, Clicks((2, 0, ClickType.QuickMove)),
                player => AssertWindowSlotEmpty(player, 2)),
            new("an input a furnace refuses", FillFurnaceRefusal, Clicks((5, 0, ClickType.QuickMove)),
                player => AssertWindowSlotEmpty(player, 0)),
            new("blaze powder topping up the fuel", FillBrewingFuel, Clicks((5, 0, ClickType.QuickMove)),
                player => AssertWindowSlot(player, BrewingStand.FuelSlot, ItemsRegistry.BlazePowder, 64)),
            new("an ingredient for a full slot", FillFullIngredient, Clicks((5, 0, ClickType.QuickMove)),
                player => AssertWindowSlot(player, BrewingStand.IngredientSlot, ItemsRegistry.NetherWart, 64)),
            new("bottles split over the bottle slots", FillBottles, Clicks((5, 0, ClickType.QuickMove)),
                player => AssertWindowSlot(player, 2, ItemsRegistry.GlassBottle, 1))
        ];

        await FixtureCases.AssertAllAsync(cases, transfer => transfer.Name, AssertTransferConservesItems);
    }

    // A shift-craft: how the grid and inventory start, the output, how many end up stored and dropped, and what's
    // left of each grid input.
    private sealed record ShiftCraft(
        string Name,
        Func<PlayerEntity, Task> Fill,
        ItemStack Output,
        int Stored,
        int Dropped,
        (short Slot, int Left)[] Inputs);

    // Clicks on a fresh player: how the window, inventory and cursor start, the clicks, and the outcome to check.
    private sealed record Transfer(
        string Name,
        Func<PlayerEntity, Task> Fill,
        Func<Server, PlayerEntity, Task> Act,
        Action<PlayerEntity> Outcome);

    private static readonly SimpleDataComponent<int> MapSeven = ComponentBuilder.MapId with { Value = 7 };

    private static async Task AssertShiftCraft(ShiftCraft craft)
    {
        await RecipesRegistry.InitializeAsync();
        await using var context = TestServerFactory.CreateServer($"shift-craft-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Crafter");
        await craft.Fill(player);
        var before = Stored(player, craft.Output);

        await ClickAsync(context.Server, player, 0, 0, ClickType.QuickMove);

        var dropped = Dropped(context.World, player)
            .Where(stack => SameItem(stack, craft.Output))
            .Sum(stack => stack.Count);
        Assert.Equal(craft.Stored, Stored(player, craft.Output) - before);
        Assert.Equal(craft.Dropped, dropped);
        foreach (var (slot, left) in craft.Inputs)
            Assert.Equal(left, player.Inventory.GetItem(slot)?.Count ?? 0);
    }

    private static async Task AssertTransferConservesItems(Transfer transfer)
    {
        await RecipesRegistry.InitializeAsync();
        await using var context = TestServerFactory.CreateServer($"transfer-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Mover");
        await transfer.Fill(player);
        var before = Totals(context.World, player);

        await transfer.Act(context.Server, player);

        var after = Totals(context.World, player);
        Assert.Equal(Describe(before), Describe(after));
        transfer.Outcome(player);
    }

    // Clicks window slots one after another through the click packet.
    private static Func<Server, PlayerEntity, Task> Clicks(params (short Slot, sbyte Button, ClickType Type)[] clicks)
    {
        return async (server, player) =>
        {
            foreach (var (slot, button, type) in clicks)
                await ClickAsync(server, player, slot, button, type);
        };
    }

    // Every item the player can reach: the open window, the inventory, the cursor and the ground nearby, by kind.
    private static Dictionary<string, int> Totals(World world, PlayerEntity player)
    {
        var stacks = new List<ItemStack?> { player.CarriedItem };
        stacks.AddRange(Slots(player.Inventory));
        if (player.OpenedContainer is BaseContainer window && window != player.Inventory)
            stacks.AddRange(Slots(window));

        stacks.AddRange(Dropped(world, player));

        return stacks
            .Where(stack => stack is { IsAir: false })
            .GroupBy(stack => stack!.Holder.UnlocalizedName)
            .ToDictionary(group => group.Key, group => group.Sum(stack => stack!.Count));
    }

    private static string Describe(Dictionary<string, int> totals) =>
        string.Join(", ", totals.OrderBy(total => total.Key).Select(total => $"{total.Key} x{total.Value}"));

    private static IEnumerable<ItemStack?> Slots(BaseContainer container) =>
        Enumerable.Range(0, container.Size).Select(container.GetItem);

    private static IEnumerable<ItemStack> Dropped(World world, PlayerEntity player) =>
        world.GetNonPlayerEntitiesInRange(player.Position, 8).OfType<ItemEntity>().Select(entity => entity.Item);

    // Output in the player's inventory outside the crafting grid and its result.
    private static int Stored(PlayerEntity player, ItemStack output)
    {
        return Enumerable.Range(5, player.Inventory.Size - 5)
            .Select(player.Inventory.GetItem)
            .Where(stack => stack is not null && SameItem(stack, output))
            .Sum(stack => stack!.Count);
    }

    private static bool SameItem(ItemStack stack, ItemStack output) =>
        new ItemStack(stack, 1) == new ItemStack(output, 1);

    private static void AssertCarries(PlayerEntity player, Item item, int count)
    {
        Assert.True(player.CarriedItem?.Is(item), $"The cursor holds {player.CarriedItem?.Holder.UnlocalizedName}.");
        Assert.Equal(count, player.CarriedItem?.Count);
        Assert.False(player.IsDragging);
    }

    private static void AssertWindowSlot(PlayerEntity player, short slot, Item item, int count)
    {
        var stack = player.OpenedContainer!.GetItem(slot);
        Assert.True(stack?.Is(item), $"Window slot {slot} holds {stack?.Holder.UnlocalizedName ?? "nothing"}.");
        Assert.Equal(count, stack?.Count);
    }

    private static void AssertWindowSlotEmpty(PlayerEntity player, short slot)
    {
        var stack = player.OpenedContainer!.GetItem(slot);
        Assert.True(stack is null or { IsAir: true }, $"Window slot {slot} holds {stack?.Holder.UnlocalizedName}.");
    }

    private static Task FillForPlanks(PlayerEntity player)
    {
        for (var slot = 9; slot < 44; slot++)
            player.Inventory.SetItem(slot, new ItemStack(ItemsRegistry.Dirt, 64));

        player.Inventory.SetItem(44, new ItemStack(ItemsRegistry.OakPlanks, 58));
        player.Inventory.SetItem(1, new ItemStack(ItemsRegistry.OakLog, 3));

        // The result the grid shows, as a click putting the logs there would have left it.
        player.Inventory.SetItem(0, new ItemStack(ItemsRegistry.OakPlanks, 4));
        return Task.CompletedTask;
    }

    private static async Task FillForMaps(PlayerEntity player)
    {
        player.Inventory.SetItem(1, new ItemStack(ItemsRegistry.FilledMap, 3, MapSeven));
        player.Inventory.SetItem(2, new ItemStack(ItemsRegistry.Map, 5));

        await MainEventHandler.RefreshCraftingResultAsync(player);
    }

    private static Task CarryDirt(PlayerEntity player)
    {
        player.CarriedItem = new ItemStack(ItemsRegistry.Dirt, 4);
        return Task.CompletedTask;
    }

    private static async Task FillBucketSwap(PlayerEntity player)
    {
        await OpenAsync(player, new SmeltingContainer(InventoryType.Furnace, 3, "furnace"));
        player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.Bucket, 3));
    }

    private static async Task FillDisplacedFuel(PlayerEntity player)
    {
        var furnace = await OpenAsync(player, new SmeltingContainer(InventoryType.Furnace, 3, "furnace"));
        furnace.SetItem(1, new ItemStack(ItemsRegistry.Coal, 4));
        player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.Bucket, 3));
        player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.Coal, 10));
        player.Inventory.SetItem(45, new ItemStack(ItemsRegistry.Coal, 62));
    }

    private static async Task FillDoubleClick(PlayerEntity player)
    {
        var chest = await OpenAsync(player, new Container());
        chest.SetItem(0, new ItemStack(ItemsRegistry.Stone, 64));
        chest.SetItem(1, new ItemStack(ItemsRegistry.Stone, 10));
        player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.Stone, 20));
        player.CarriedItem = new ItemStack(ItemsRegistry.Stone, 1);
    }

    private static async Task FillFurnaceOutput(PlayerEntity player)
    {
        var furnace = await OpenAsync(player, new SmeltingContainer(InventoryType.Furnace, 3, "furnace"));
        furnace.SetItem(2, new ItemStack(ItemsRegistry.IronIngot, 5));
    }

    // The window numbers the furnace's slots 0 to 2, then the player's inventory from 9 at 3.
    private static async Task FillFurnaceRefusal(PlayerEntity player)
    {
        await OpenAsync(player, new SmeltingContainer(InventoryType.Furnace, 3, "furnace"));
        player.Inventory.SetItem(11, new ItemStack(ItemsRegistry.Dirt, 3));
    }

    // The window numbers the stand's bottles 0 to 2, its ingredient 3 and its fuel 4, then the player's inventory
    // from 9 at 5.
    private static async Task FillBrewingFuel(PlayerEntity player)
    {
        var stand = await OpenAsync(player, new BrewingStand());
        stand.SetItem(BrewingStand.FuelSlot, new ItemStack(ItemsRegistry.BlazePowder, 60));
        player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.BlazePowder, 10));
    }

    private static async Task FillFullIngredient(PlayerEntity player)
    {
        var stand = await OpenAsync(player, new BrewingStand());
        stand.SetItem(BrewingStand.IngredientSlot, new ItemStack(ItemsRegistry.NetherWart, 64));
        player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.NetherWart, 5));
    }

    private static async Task FillBottles(PlayerEntity player)
    {
        await OpenAsync(player, new BrewingStand());
        player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.GlassBottle, 5));
    }
}
