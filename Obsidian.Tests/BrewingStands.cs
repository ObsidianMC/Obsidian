using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Commands.Framework;
using Obsidian.Events;
using Obsidian.Nbt;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Brewing stands: shift-clicking routes stacks to their slots like vanilla's brewing menu, they brew vanilla's mixes
/// with vanilla's times, and plugins' mixes brew until their plugin unloads.
/// </summary>
// Loading the probe plugin merges its tags into vanilla's, which PluginTagFiles changes too.
[Collection(nameof(PluginTagFiles))]
public sealed class BrewingStands : IDisposable
{
    private static readonly FakeServer server = new();

    // Each row fills the stand and the player's inventory, shift-clicks a window slot and checks where things went.
    // The window numbers the stand's bottles 0 to 2, its ingredient 3 and its fuel 4, then the player's inventory
    // from 9 at 5.
    private static readonly Dictionary<string, (Action<BrewingStand, Container> Fill, short Slot,
        Action<BrewingStand, Container> Check)> quickMoves = new()
    {
        ["Blaze powder tops up the fuel and stops there"] = (
            (stand, inventory) =>
            {
                stand.SetItem(BrewingStand.FuelSlot, new ItemStack(ItemsRegistry.BlazePowder, 60));
                inventory.SetItem(9, new ItemStack(ItemsRegistry.BlazePowder, 10));
            },
            5,
            (stand, inventory) =>
            {
                Assert.Equal(64, stand.GetItem(BrewingStand.FuelSlot)?.Count);
                Assert.Null(stand.GetItem(BrewingStand.IngredientSlot));
                Assert.Equal(6, inventory.GetItem(9)?.Count);
            }),
        ["Blaze powder goes to the ingredient slot when the fuel is full"] = (
            (stand, inventory) =>
            {
                stand.SetItem(BrewingStand.FuelSlot, new ItemStack(ItemsRegistry.BlazePowder, 64));
                inventory.SetItem(9, new ItemStack(ItemsRegistry.BlazePowder, 10));
            },
            5,
            (stand, inventory) =>
            {
                Assert.Equal(10, stand.GetItem(BrewingStand.IngredientSlot)?.Count);
                Assert.Null(inventory.GetItem(9));
            }),
        ["An ingredient goes to the ingredient slot"] = (
            (stand, inventory) => inventory.SetItem(40, new ItemStack(ItemsRegistry.NetherWart, 5)),
            36,
            (stand, inventory) =>
            {
                Assert.True(stand.GetItem(BrewingStand.IngredientSlot)?.Is(ItemsRegistry.NetherWart));
                Assert.Null(inventory.GetItem(40));
            }),
        ["An ingredient stays put when the ingredient slot is full"] = (
            (stand, inventory) =>
            {
                stand.SetItem(BrewingStand.IngredientSlot, new ItemStack(ItemsRegistry.NetherWart, 64));
                inventory.SetItem(9, new ItemStack(ItemsRegistry.NetherWart, 5));
            },
            5,
            (stand, inventory) =>
            {
                Assert.Equal(5, inventory.GetItem(9)?.Count);
                Assert.Null(inventory.GetItem(36));
            }),
        ["Bottles go one to each bottle slot"] = (
            (stand, inventory) => inventory.SetItem(9, new ItemStack(ItemsRegistry.GlassBottle, 5)),
            5,
            (stand, inventory) =>
            {
                for (var slot = 0; slot < 3; slot++)
                    Assert.Equal(1, stand.GetItem(slot)?.Count);

                Assert.Equal(2, inventory.GetItem(9)?.Count);
            }),
        ["Other stacks go from the main inventory to the hotbar"] = (
            (stand, inventory) => inventory.SetItem(9, new ItemStack(ItemsRegistry.Dirt, 3)),
            5,
            (stand, inventory) => Assert.Equal(3, inventory.GetItem(36)?.Count)),
        ["Other stacks go from the hotbar to the main inventory"] = (
            (stand, inventory) => inventory.SetItem(36, new ItemStack(ItemsRegistry.Dirt, 3)),
            32,
            (stand, inventory) => Assert.Equal(3, inventory.GetItem(9)?.Count)),
        ["The stand's stacks go to the hotbar's last slot first"] = (
            (stand, inventory) => stand.SetItem(1, Potion(ItemsRegistry.Potion, API.Potion.Awkward)),
            1,
            (stand, inventory) =>
            {
                Assert.Null(stand.GetItem(1));
                Assert.True(inventory.GetItem(44)?.Is(ItemsRegistry.Potion));
            })
    };

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-brewing-{Guid.NewGuid():N}");

    public BrewingStands() => Directory.CreateDirectory(this.folder);

    public void Dispose() => Directory.Delete(this.folder, recursive: true);

    public static TheoryData<string> QuickMoveRows => [.. quickMoves.Keys];

    [Theory(DisplayName = "Shift-clicking in a brewing stand routes stacks like vanilla's brewing menu")]
    [MemberData(nameof(QuickMoveRows))]
    public void QuickMoveRoutesLikeVanilla(string row)
    {
        var (fill, slot, check) = quickMoves[row];
        var stand = new BrewingStand();
        var player = new FakePlayer { OpenedContainer = stand };

        fill(stand, player.Inventory);

        var (clicked, forPlayer) = stand.GetSlot(slot);
        MainEventHandler.OnInventoryClickTest(new ContainerClickEventArgs(
            player,
            server,
            forPlayer ? player.Inventory : stand)
        {
            ContainerId = 1,
            ClickedSlot = clicked,
            WindowSlot = slot,
            Button = 0,
            StateId = 0,
            ClickType = ClickType.QuickMove
        });

        check(stand, player.Inventory);
    }

    [Fact(DisplayName = "A brew takes fuel, brews for 400 ticks and mixes every bottle")]
    public void BrewCompletes()
    {
        var stand = new BrewingStand();
        stand.SetItem(0, Potion(ItemsRegistry.Potion, API.Potion.Water));
        stand.SetItem(2, Potion(ItemsRegistry.Potion, API.Potion.Water));
        stand.SetItem(BrewingStand.IngredientSlot, new ItemStack(ItemsRegistry.NetherWart, 2));
        stand.SetItem(BrewingStand.FuelSlot, new ItemStack(ItemsRegistry.BlazePowder, 1));

        // The first tick takes a blaze powder for 20 brews and starts one, using a brew of it up.
        var first = Brewing.Tick(stand);
        Assert.Null(stand.GetItem(BrewingStand.FuelSlot));
        Assert.Equal(19, stand.Fuel);
        Assert.Equal(400, stand.BrewTime);
        Assert.Equal([true, false, true], first.Bottles);

        for (var tick = 1; tick < 400; tick++)
            Assert.False(Brewing.Tick(stand).Brewed);

        Assert.True(Brewing.Tick(stand).Brewed);
        Assert.Equal(API.Potion.Awkward, PotionOf(stand.GetItem(0)));
        Assert.Equal(API.Potion.Awkward, PotionOf(stand.GetItem(2)));
        Assert.Null(stand.GetItem(1));
        Assert.Equal(1, stand.GetItem(BrewingStand.IngredientSlot)?.Count);
        Assert.Equal(0, stand.BrewTime);

        // Gunpowder makes splash potions of them, keeping their potion; the stand then has nothing left to brew.
        stand.SetItem(BrewingStand.IngredientSlot, new ItemStack(ItemsRegistry.Gunpowder, 1));
        for (var tick = 0; tick <= 400; tick++)
            Brewing.Tick(stand);

        Assert.True(stand.GetItem(0)?.Is(ItemsRegistry.SplashPotion));
        Assert.Equal(API.Potion.Awkward, PotionOf(stand.GetItem(0)));
        Assert.Null(stand.GetItem(BrewingStand.IngredientSlot));
        Assert.Equal(18, stand.Fuel);
    }

    [Fact(DisplayName = "A brew loaded part way through finishes without spending more fuel")]
    public void LoadedBrewFinishes()
    {
        var items = new NbtList(NbtTagType.Compound, "Items")
        {
            Slotted(Potion(ItemsRegistry.Potion, API.Potion.Water), 0),
            Slotted(new ItemStack(ItemsRegistry.NetherWart, 1), BrewingStand.IngredientSlot)
        };

        var tag = new NbtCompound
        {
            items,
            new NbtTag<short>("BrewTime", 100),
            new NbtTag<byte>("Fuel", 5)
        };

        var stand = ((BrewingStandBlockEntity)BlockEntityNbt.Load(tag, "minecraft:brewing_stand", Vector.Zero)).Stand;

        for (var tick = 1; tick < 100; tick++)
            Assert.False(Brewing.Tick(stand).Brewed);

        Assert.True(Brewing.Tick(stand).Brewed);
        Assert.Equal(API.Potion.Awkward, PotionOf(stand.GetItem(0)));
        Assert.Equal(5, stand.Fuel);
    }

    [Fact(DisplayName = "A plugin's potion mix brews until the plugin unloads",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task PluginMixBrewsUntilUnloaded()
    {
        await using var context = TestServerFactory.CreateServer($"brewing-{Guid.NewGuid():N}");
        await using var manager = new PluginManager(
            context.Server,
            context.Provider,
            context.EventDispatcher,
            context.Provider.GetRequiredService<CommandHandler>(),
            NullLogger<PluginManager>.Instance,
            new ConfigurationBuilder().Build());

        var path = Path.Join(this.folder, "probe.obby");
        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0"));
        var container = (await manager.LoadPluginAsync(path))!;

        Assert.True(ItemsRegistry.TryGet("probe:gem", out var gem));
        var awkward = Potion(ItemsRegistry.SplashPotion, API.Potion.Awkward);
        var stand = new BrewingStand();
        var player = new FakePlayer { OpenedContainer = stand };

        // Its gem is an ingredient now: shift-clicking sends it to the ingredient slot, and it brews.
        player.Inventory.SetItem(9, new ItemStack(gem, 4));
        MainEventHandler.OnInventoryClickTest(new ContainerClickEventArgs(player, server, player.Inventory)
        {
            ContainerId = 1,
            ClickedSlot = 9,
            WindowSlot = 5,
            Button = 0,
            StateId = 0,
            ClickType = ClickType.QuickMove
        });

        Assert.Equal(4, stand.GetItem(BrewingStand.IngredientSlot)?.Count);

        stand.SetItem(0, awkward);
        stand.Fuel = 1;
        for (var tick = 0; tick <= 400; tick++)
            Brewing.Tick(stand);

        Assert.True(stand.GetItem(0)?.Is(ItemsRegistry.SplashPotion));
        Assert.Equal(API.Potion.Luck, PotionOf(stand.GetItem(0)));

        await manager.UnloadPluginAsync(container);

        Assert.False(BrewingRegistry.IsIngredient(new ItemStack(gem, 1)));
        Assert.False(BrewingRegistry.HasMix(awkward, new ItemStack(gem, 1)));
        Assert.DoesNotContain(BrewingRegistry.PotionMixes, mix => mix.Ingredient == gem);
    }

    private static ItemStack Potion(Item bottle, API.Potion potion) =>
        new(bottle, 1, new PotionContentsDataComponent { Potion = potion });

    private static NbtCompound Slotted(ItemStack item, int slot)
    {
        var tag = item.ToNbt();
        tag.Add(new NbtTag<byte>("Slot", (byte)slot));

        return tag;
    }

    private static API.Potion? PotionOf(ItemStack? bottle) =>
        bottle?.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents)?.Potion;
}
