using System.Collections.Immutable;
using Obsidian.API;
using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Events;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Clicks in menus, which number their own slots then the player's inventory like vanilla's: a plugin's, a furnace's
/// and the player's own inventory; and opening and closing them.
/// </summary>
public sealed class MenuClicks
{
    private static readonly FakeServer server = new();

    // One row of five slots, so the window's slot 5 is the player's inventory slot 9.
    private static readonly MenuDefinition pouch = MenuTypesRegistry.Register("clicks:pouch", 1, 5);

    [Fact]
    public void EqualCustomDataStacksMerge()
    {
        var first = ComponentBuilder.CustomData with
        {
            Value = new NbtCompound
            {
                new NbtTag<int>("level", 7),
                new NbtTag<string>("owner", "Alex")
            }
        };
        var second = ComponentBuilder.CustomData with
        {
            Value = new NbtCompound
            {
                new NbtTag<string>("owner", "Alex"),
                new NbtTag<int>("level", 7)
            }
        };
        var stored = new ItemStack(ItemsRegistry.Stone, 5, first);
        var carried = new ItemStack(ItemsRegistry.Stone, 3, second);
        Assert.Equal(first, first with { Value = first.Value });
        Assert.Equal(stored, carried);
        Assert.Equal(stored.GetHashCode(), carried.GetHashCode());

        var menu = new MenuContainer(pouch, "Pouch");
        menu.SetItem(0, stored);
        var player = new FakePlayer
        {
            OpenedContainer = menu,
            CarriedItem = carried
        };

        Click(player, 0, 0, ClickType.Pickup);

        Assert.Equal(8, menu.GetItem(0)?.Count);
        Assert.Null(player.CarriedItem);
        Assert.All(menu.Skip(1), Assert.Null);

        var different = new ItemStack(ItemsRegistry.Stone, 3, ComponentBuilder.CustomData with
        {
            Value = new NbtCompound { new NbtTag<int>("level", 8) }
        });
        Assert.NotEqual(stored, different);
    }

    [Fact(DisplayName = "A survival player's full-stack drag creates no items")]
    public void SurvivalMiddleDragCreatesNothing()
    {
        var menu = new MenuContainer(pouch, "Pouch");
        var player = new FakePlayer
        {
            OpenedContainer = menu,
            CarriedItem = new ItemStack(ItemsRegistry.Diamond, 1)
        };

        // Vanilla's buttons: start, add two slots, end, with the middle button's drag.
        Click(player, -999, 8, ClickType.QuickCraft);
        Click(player, 0, 9, ClickType.QuickCraft);
        Click(player, 1, 9, ClickType.QuickCraft);
        Click(player, -999, 10, ClickType.QuickCraft);

        Assert.All(menu, Assert.Null);
        Assert.Equal(1, player.CarriedItem.Count);
        Assert.False(player.IsDragging);
        Assert.Empty(player.DraggedSlots);
    }

    [Fact(DisplayName = "A drag spreads the carried stack across the menu and the player's inventory")]
    public void DragReachesPlayerInventory()
    {
        var menu = new MenuContainer(pouch, "Pouch");
        var player = new FakePlayer
        {
            OpenedContainer = menu,
            CarriedItem = new ItemStack(ItemsRegistry.Stone, 4)
        };

        Click(player, -999, 0, ClickType.QuickCraft);
        Click(player, 0, 1, ClickType.QuickCraft);
        Click(player, 5, 1, ClickType.QuickCraft);
        Click(player, -999, 2, ClickType.QuickCraft);

        Assert.Equal(2, menu.GetItem(0)?.Count);
        Assert.Equal(2, player.Inventory.GetItem(9)?.Count);
        Assert.All(menu.Skip(1), Assert.Null);
        Assert.Null(player.CarriedItem);
        Assert.False(player.IsDragging);
    }

    [Fact(DisplayName = "Shift-clicking moves whole stacks between the menu and the player's inventory")]
    public void QuickMoveCrossesToTheOtherInventory()
    {
        var menu = new MenuContainer(pouch, "Pouch");
        var player = new FakePlayer { OpenedContainer = menu };
        var named = new ItemStack(ItemsRegistry.Stone, 5, ComponentBuilder.CustomName with { Value = "Named stone" });
        menu.SetItem(0, named);

        // Like vanilla's chest, to the hotbar's last slot first.
        Click(player, 0, 0, ClickType.QuickMove);

        Assert.Null(menu.GetItem(0));
        Assert.Equal(named, player.Inventory.GetItem(44));
        Assert.Equal(5, player.Inventory.GetItem(44)?.Count);

        Click(player, 40, 0, ClickType.QuickMove);

        Assert.Null(player.Inventory.GetItem(44));
        Assert.Equal(named, menu.GetItem(0));
    }

    [Fact(DisplayName = "Viewers of a shared menu take turns and see each other's clicks",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task SharedMenuClicksTakeTurns()
    {
        await using var context = TestServerFactory.CreateServer($"menu-clicks-{Guid.NewGuid():N}");
        var first = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "First");
        var second = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Second");
        Obsidian.Entities.Player[] players = [first, second];
        var menu = new MenuContainer(pouch, "Pouch");
        menu.SetItem(0, new ItemStack(ItemsRegistry.Diamond, 10));

        foreach (var player in players)
            await player.OpenInventoryAsync(menu);

        Assert.Equal(2, menu.Viewers.Count);

        await Task.WhenAll(players.Select(player => Task.Run(() => PickUpAsync(player))));

        Assert.Null(menu.GetItem(0));
        Assert.Equal(10, players.Sum(player => player.CarriedItem?.Count ?? 0));

        // The one holding the stack puts it back; the other sees it there.
        var holder = players.Single(player => player.CarriedItem is not null);
        var other = players.Single(player => player != holder);
        await PickUpAsync(holder);

        var shown = ((TestClient)other.Client).SentPackets.OfType<ContainerSetContentPacket>().Last();
        Assert.Equal(other.CurrentContainerId, shown.ContainerId);
        Assert.Equal(10, shown.Items[0]?.Count);

        async Task PickUpAsync(Obsidian.Entities.Player player)
        {
            var click = Packet(player.CurrentContainerId, 0, 0, ClickType.Pickup);
            await click.HandleAsync(context.Server, player);
        }
    }

    [Fact(DisplayName = "Shift-clicking in a furnace routes stacks like vanilla's furnace menu")]
    public async Task FurnaceQuickMoveRoutesLikeVanilla()
    {
        await RecipesRegistry.InitializeAsync();

        var furnace = new SmeltingContainer(InventoryType.Furnace, 3, "furnace");
        var player = new FakePlayer { OpenedContainer = furnace };

        // The window numbers the furnace's ingredient, fuel and result 0 to 2, then the player's inventory from 9.
        player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.RawIron, 4));
        player.Inventory.SetItem(10, new ItemStack(ItemsRegistry.Coal, 2));
        player.Inventory.SetItem(11, new ItemStack(ItemsRegistry.Dirt, 3));
        player.Inventory.SetItem(12, new ItemStack(ItemsRegistry.OakLog, 1));
        furnace.SetItem(2, new ItemStack(ItemsRegistry.IronIngot, 5));

        Click(player, 3, 0, ClickType.QuickMove);
        Click(player, 4, 0, ClickType.QuickMove);
        Click(player, 5, 0, ClickType.QuickMove);

        Assert.Equal(4, furnace.GetItem(0)?.Count);
        Assert.Equal(2, furnace.GetItem(1)?.Count);
        Assert.True(furnace.GetItem(1)?.Is(ItemsRegistry.Coal));

        // What neither cooks nor burns goes from the main inventory to the hotbar.
        Assert.Equal(3, player.Inventory.GetItem(36)?.Count);

        // A log cooks into charcoal, so it waits for the ingredient slot even though it burns.
        Click(player, 6, 0, ClickType.QuickMove);
        Assert.Equal(1, player.Inventory.GetItem(12)?.Count);

        // The result goes to the hotbar's last slot first.
        Click(player, 2, 0, ClickType.QuickMove);
        Assert.Null(furnace.GetItem(2));
        Assert.Equal(5, player.Inventory.GetItem(44)?.Count);
    }

    [Fact(DisplayName = "Drags in the player's inventory follow vanilla's drag order and slot rules",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task InventoryDragNeedsAStartAndKeepsSlotRules()
    {
        await using var context = TestServerFactory.CreateServer($"inventory-drag-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Dragger");
        player.CarriedItem = new ItemStack(ItemsRegistry.Dirt, 4);

        // Slots added and a drag ended without starting one place nothing.
        await ClickAsync(1, 1);
        await ClickAsync(2, 1);
        await ClickAsync(-999, 2);

        Assert.Null(player.Inventory.GetItem(1));
        Assert.Null(player.Inventory.GetItem(2));
        Assert.Equal(4, player.CarriedItem?.Count);

        // A started drag skips the crafting result and the armour slots, which don't take dirt.
        await ClickAsync(-999, 0);
        await ClickAsync(0, 1);
        await ClickAsync(5, 1);
        await ClickAsync(1, 1);
        await ClickAsync(2, 1);
        await ClickAsync(-999, 2);

        Assert.Equal(2, player.Inventory.GetItem(1)?.Count);
        Assert.Equal(2, player.Inventory.GetItem(2)?.Count);
        Assert.Null(player.Inventory.GetItem(5));
        Assert.Null(player.CarriedItem);
        Assert.False(player.IsDragging);

        async Task ClickAsync(short slot, sbyte button) =>
            await Packet(0, slot, button, ClickType.QuickCraft).HandleAsync(context.Server, player);
    }

    [Fact(DisplayName = "Opening a container syncs the cursor, and closing it from the server closes the screen",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task OpenSyncsCursorAndForcedCloseClosesScreen()
    {
        await using var context = TestServerFactory.CreateServer($"menu-close-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Closer");
        var client = (TestClient)player.Client;
        player.CarriedItem = new ItemStack(ItemsRegistry.Stone, 3);

        // Even an empty chest's contents are sent, as they carry the cursor.
        await player.OpenInventoryAsync(new Container());
        var id = player.CurrentContainerId;

        var contents = client.SentPackets.OfType<ContainerSetContentPacket>().Last();
        Assert.Equal(id, contents.ContainerId);
        Assert.Equal(3, contents.CarriedItem?.Count);

        // The server's close handler lets go of the container, but the client's screen still closes.
        await player.CloseContainerAsync();

        Assert.Null(player.OpenedContainer);
        var closed = client.SentPackets.OfType<Net.Packets.Play.Clientbound.ContainerClosePacket>().Single();
        Assert.Equal(id, closed.ContainerId);
    }

    [Fact(DisplayName = "Players opening a new chest at once share its one stored container",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task FirstOpenOfAChestSharesOneContainer()
    {
        await using var context = TestServerFactory.CreateServer($"chest-open-{Guid.NewGuid():N}");
        Obsidian.Entities.Player[] players =
        [
            TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "First"),
            TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Second")
        ];
        var position = new Vector(0, 64, 0);

        await Task.WhenAll(players.Select(player => Task.Run(async () =>
            await context.Server.EventDispatcher.ExecuteEventAsync(new PlayerInteractEventArgs(player, context.Server)
            {
                Block = BlocksRegistry.Chest,
                BlockLocation = position
            }))));

        var stored = Assert.IsAssignableFrom<BaseContainer>(await context.World.GetBlockEntityAsync(position));
        Assert.All(players, player => Assert.Same(stored, player.OpenedContainer));
        Assert.Equal(2, stored.Viewers.Count);
    }

    // Rows of vanilla's click behaviour, each a click sequence on a fresh player, played through the click packet.
    private static readonly Dictionary<string, Func<Server, Obsidian.Entities.Player, Task>> clickRows = new()
    {
        ["Right-click with an empty cursor takes half the stack, rounded up"] = async (server, player) =>
        {
            var chest = await OpenAsync(player, new Container());
            chest.SetItem(0, new ItemStack(ItemsRegistry.Dirt, 5));

            await ClickAsync(server, player, 0, 1, ClickType.Pickup);

            Assert.Equal(3, player.CarriedItem?.Count);
            Assert.Equal(2, chest.GetItem(0)?.Count);
        },
        ["Right-click with another item swaps it with the slot's"] = async (server, player) =>
        {
            var chest = await OpenAsync(player, new Container());
            chest.SetItem(0, new ItemStack(ItemsRegistry.Dirt, 5));
            player.CarriedItem = new ItemStack(ItemsRegistry.Stone, 3);

            await ClickAsync(server, player, 0, 1, ClickType.Pickup);

            Assert.True(chest.GetItem(0)?.Is(ItemsRegistry.Stone));
            Assert.Equal(3, chest.GetItem(0)?.Count);
            Assert.True(player.CarriedItem?.Is(ItemsRegistry.Dirt));
            Assert.Equal(5, player.CarriedItem?.Count);
        },
        ["A number key puts only armour in an armour slot"] = async (server, player) =>
        {
            player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.Dirt, 5));
            player.Inventory.SetItem(37, new ItemStack(ItemsRegistry.DiamondHelmet));

            await ClickAsync(server, player, 5, 0, ClickType.Swap);
            Assert.Null(player.Inventory.GetItem(5));
            Assert.Equal(5, player.Inventory.GetItem(36)?.Count);

            await ClickAsync(server, player, 5, 1, ClickType.Swap);
            Assert.True(player.Inventory.GetItem(5)?.Is(ItemsRegistry.DiamondHelmet));
            Assert.Null(player.Inventory.GetItem(37));
        },
        ["A number key fills a furnace's fuel slot with one bucket"] = async (server, player) =>
        {
            var furnace = await OpenAsync(player, new SmeltingContainer(InventoryType.Furnace, 3, "furnace"));
            player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.Bucket, 3));

            await ClickAsync(server, player, 1, 0, ClickType.Swap);

            Assert.Equal(1, furnace.GetItem(1)?.Count);
            Assert.Equal(2, player.Inventory.GetItem(36)?.Count);
        },
        ["A number key's displaced stack tops up the off hand before the main inventory"] = async (server, player) =>
        {
            var furnace = await OpenAsync(player, new SmeltingContainer(InventoryType.Furnace, 3, "furnace"));
            furnace.SetItem(1, new ItemStack(ItemsRegistry.Coal, 4));
            player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.Bucket, 3));
            player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.Coal, 10));
            player.Inventory.SetItem(45, new ItemStack(ItemsRegistry.Coal, 62));

            // One bucket takes the fuel slot; the coal goes where vanilla's inventory adds it: the off hand, then on.
            await ClickAsync(server, player, 1, 0, ClickType.Swap);

            Assert.True(furnace.GetItem(1)?.Is(ItemsRegistry.Bucket));
            Assert.Equal(2, player.Inventory.GetItem(36)?.Count);
            Assert.Equal(64, player.Inventory.GetItem(45)?.Count);
            Assert.Equal(12, player.Inventory.GetItem(9)?.Count);
        },
        ["Double-click collects partial stacks first, from the window's first slot"] = async (server, player) =>
        {
            var chest = await OpenAsync(player, new Container());
            chest.SetItem(0, new ItemStack(ItemsRegistry.Stone, 64));
            chest.SetItem(1, new ItemStack(ItemsRegistry.Stone, 10));
            player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.Stone, 20));
            player.CarriedItem = new ItemStack(ItemsRegistry.Stone, 1);

            await ClickAsync(server, player, 2, 0, ClickType.PickupAll);

            Assert.Equal(64, player.CarriedItem?.Count);
            Assert.Null(chest.GetItem(1));
            Assert.Null(player.Inventory.GetItem(9));
            Assert.Equal(31, chest.GetItem(0)?.Count);
        },
        ["Shift-crafting moves what fits, drops the rest and stops when nothing fits"] = async (server, player) =>
        {
            for (var slot = 9; slot < 44; slot++)
                player.Inventory.SetItem(slot, new ItemStack(ItemsRegistry.Dirt, 64));

            player.Inventory.SetItem(44, new ItemStack(ItemsRegistry.OakPlanks, 58));
            player.Inventory.SetItem(1, new ItemStack(ItemsRegistry.OakLog, 3));

            // The result the grid shows, as a click putting the logs there would have left it.
            player.Inventory.SetItem(0, new ItemStack(ItemsRegistry.OakPlanks, 4));

            // Four planks fit, then two of the next four, then none.
            await ClickAsync(server, player, 0, 0, ClickType.QuickMove);

            Assert.Equal(64, player.Inventory.GetItem(44)?.Count);
            Assert.Equal(1, player.Inventory.GetItem(1)?.Count);
        }
    };

    public static TheoryData<string> ClickRows => [.. clickRows.Keys];

    [Theory(DisplayName = "Clicks move items like vanilla's menus", Timeout = TestServerFactory.TestTimeout)]
    [MemberData(nameof(ClickRows))]
    public async Task ClicksMoveItemsLikeVanilla(string row)
    {
        await RecipesRegistry.InitializeAsync();
        await using var context = TestServerFactory.CreateServer($"clicks-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Clicker");

        await clickRows[row](context.Server, player);
    }

    [Fact(DisplayName = "A click whose slots changed while plugins decided isn't applied, and is corrected",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task StaleOrCancelledClicksAreCorrected()
    {
        await using var context = TestServerFactory.CreateServer($"stale-click-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Clicker");
        var client = (TestClient)player.Client;
        var menu = await OpenAsync(player, new MenuContainer(pouch, "Pouch"));
        menu.SetItem(0, new ItemStack(ItemsRegistry.Diamond, 10));

        var interfere = true;
        var changed = false;
        context.EventDispatcher.RegisterEvent(null, (Func<ContainerClickEventArgs, ValueTask>)(click =>
        {
            if (interfere)
            {
                // Another change lands while the handler decides; its change, registered with the click, mustn't run.
                menu.SetItem(0, new ItemStack(ItemsRegistry.Stone, 1));
                click.ApplyWithClick(() => changed = true);
            }
            else
            {
                click.Cancel();
                click.ApplyWithClick(() => menu.SetItem(1, new ItemStack(ItemsRegistry.GoldIngot, 2)));
            }

            return ValueTask.CompletedTask;
        }), Priority.Normal);

        await ClickAsync(context.Server, player, 0, 0, ClickType.Pickup);

        Assert.Null(player.CarriedItem);
        Assert.False(changed);
        Assert.True(menu.GetItem(0)?.Is(ItemsRegistry.Stone));
        Assert.True(client.SentPackets.OfType<ContainerSetContentPacket>().Last().Items[0]?.Is(ItemsRegistry.Stone));

        // A cancelled click applies only the change registered with it, and the window is sent all the same.
        interfere = false;
        var sent = client.SentPackets.Count;
        await ClickAsync(context.Server, player, 0, 0, ClickType.Pickup);

        Assert.Null(player.CarriedItem);
        Assert.Equal(2, menu.GetItem(1)?.Count);
        var shown = client.SentPackets.Skip(sent).OfType<ContainerSetContentPacket>().Single();
        Assert.Equal(2, shown.Items[1]?.Count);
        Assert.Null(shown.CarriedItem);
    }

    [Fact(DisplayName = "Creative slot packets need creative mode and set the player's inventory",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task CreativeSlotsNeedCreativeMode()
    {
        await using var context = TestServerFactory.CreateServer($"creative-slot-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Builder");
        var chest = await OpenAsync(player, new Container());
        player.GameMode = GameMode.Survival;

        await CreativeSlot(36, new ItemStack(ItemsRegistry.Diamond, 3)).HandleAsync(context.Server, player);
        Assert.Null(player.Inventory.GetItem(36));

        // In creative mode it sets the inventory's slot, whatever window is open, but not with an oversized stack.
        player.GameMode = GameMode.Creative;
        await CreativeSlot(36, new ItemStack(ItemsRegistry.Diamond, 3)).HandleAsync(context.Server, player);
        await CreativeSlot(37, new ItemStack(ItemsRegistry.Diamond, 65)).HandleAsync(context.Server, player);

        Assert.Equal(3, player.Inventory.GetItem(36)?.Count);
        Assert.Null(player.Inventory.GetItem(37));
        Assert.All(chest, Assert.Null);
    }

    [Fact(DisplayName = "A double chest opens as one 54-slot window over both halves, which viewers share",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task DoubleChestSharesBothHalves()
    {
        await using var context = TestServerFactory.CreateServer($"double-chest-{Guid.NewGuid():N}");
        var chunk = new Chunk(0, 0);
        chunk.SetChunkStatus(ChunkGenStage.full);
        context.World.LoadRegionByChunk(0, 0).SetChunk(chunk);

        // Facing north, a left half's other half is east of it; the right half's slots come first.
        var left = BlocksRegistry.Chest.WithProperty("facing", "north").WithProperty("type", "left");
        var right = BlocksRegistry.Chest.WithProperty("facing", "north").WithProperty("type", "right");
        chunk.SetBlock(0, 64, 0, left);
        chunk.SetBlock(1, 64, 0, right);

        var first = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "First");
        var second = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Second");
        await InteractAsync(first, left, new Vector(0, 64, 0));
        await InteractAsync(second, right, new Vector(1, 64, 0));

        var leftHalf = await context.World.GetBlockEntityAsync(new Vector(0, 64, 0));
        var rightHalf = await context.World.GetBlockEntityAsync(new Vector(1, 64, 0));
        foreach (var player in new[] { first, second })
        {
            var window = Assert.IsType<DoubleChestContainer>(player.OpenedContainer);
            Assert.Equal(54, window.Size);
            Assert.Same(rightHalf, window.First);
            Assert.Same(leftHalf, window.Second);
        }

        // A click in the left half reaches its block entity and the other viewer's window.
        first.CarriedItem = new ItemStack(ItemsRegistry.Diamond, 3);
        await ClickAsync(context.Server, first, 27, 0, ClickType.Pickup);

        Assert.Equal(3, ((BaseContainer)leftHalf).GetItem(0)?.Count);
        var shown = ((TestClient)second.Client).SentPackets.OfType<ContainerSetContentPacket>().Last();
        Assert.Equal(second.CurrentContainerId, shown.ContainerId);
        Assert.Equal(3, shown.Items[27]?.Count);

        async Task InteractAsync(Obsidian.Entities.Player player, IBlock block, Vector position) =>
            await context.Server.EventDispatcher.ExecuteEventAsync(new PlayerInteractEventArgs(player, context.Server)
            {
                Block = block,
                BlockLocation = position
            });
    }

    [Fact(DisplayName = "An item picked up while plugins decide makes the click stale, and Item shares nothing",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task PickupWhileDecidingRejectsTheClick()
    {
        await using var context = TestServerFactory.CreateServer($"pickup-click-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Clicker");
        var chest = await OpenAsync(player, new Container());
        var sword = new ItemStack(ItemsRegistry.IronSword);
        sword[DataComponentType.Damage] = ComponentBuilder.Damage with { Value = 3 };
        chest.SetItem(0, sword);
        player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.Diamond, 10));

        var changed = false;
        context.EventDispatcher.RegisterEvent(null, (Func<ContainerClickEventArgs, ValueTask>)(click =>
        {
            // Replacing a component on the copy reaches neither the slot nor the next read.
            click.Item![DataComponentType.Damage] = ComponentBuilder.Damage with { Value = 100 };
            Assert.Equal(3, click.Item.Damage);

            // A pickup tops up the held stack in place while the handler decides.
            Obsidian.Entities.Player.AddToInventory(player.Inventory, 36, new ItemStack(ItemsRegistry.Diamond, 1));
            click.ApplyWithClick(() => changed = true);

            return ValueTask.CompletedTask;
        }), Priority.Normal);

        await ClickAsync(context.Server, player, 0, 0, ClickType.Pickup);

        Assert.Null(player.CarriedItem);
        Assert.False(changed);
        Assert.Same(sword, chest.GetItem(0));
        Assert.Equal(3, sword.Damage);
        Assert.Equal(11, player.Inventory.GetItem(36)?.Count);
    }

    [Fact(DisplayName = "Replacing a slot's component advances its revision and corrects an in-flight click",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task ReplacingComponentInvalidatesClickAndReachesViewer()
    {
        await using var context = TestServerFactory.CreateServer($"component-click-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Clicker");
        var food = new FoodDataComponent
        {
            Nutrition = 1,
            SaturationModifier = 0.5f,
            CanAlwaysEat = false
        };
        var stack = new ItemStack(ItemsRegistry.Apple, 1, food);
        var chest = new Container();
        chest.SetItem(0, stack);
        await OpenAsync(player, chest);
        var revision = chest.Revision;
        var client = (TestClient)player.Client;
        var sent = client.SentPackets.Count;
        var applied = false;

        context.EventDispatcher.RegisterEvent(null, (Func<ContainerClickEventArgs, ValueTask>)(click =>
        {
            stack[DataComponentType.Food] = food with { Nutrition = 5 };
            click.ApplyWithClick(() => applied = true);
            return ValueTask.CompletedTask;
        }), Priority.Normal);

        await ClickAsync(context.Server, player, 0, 0, ClickType.Pickup);

        Assert.True(chest.Revision > revision);
        Assert.False(applied);
        Assert.Null(player.CarriedItem);
        Assert.Same(stack, chest.GetItem(0));
        Assert.Equal(1, food.Nutrition);
        var shown = client.SentPackets.Skip(sent).OfType<ContainerSetContentPacket>().Last();
        var updated = shown.Items[0]!.GetComponent<FoodDataComponent>(DataComponentType.Food)!;
        Assert.Equal(5, updated.Nutrition);

        // The actual wire value and a captured packet remain independent of subsequent replacements.
        stack[DataComponentType.Food] = food with { Nutrition = 9 };
        var wire = new NetworkBuffer();
        wire.WriteItemStack(shown.Items[0]);
        wire.Reset();
        Assert.Equal(5, wire.ReadItemStack()!.GetComponent<FoodDataComponent>(DataComponentType.Food)!.Nutrition);
    }

    [Fact(DisplayName = "A crafting click whose result no longer follows the grid is corrected, not applied",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task StaleCraftingResultIsCorrected()
    {
        await RecipesRegistry.InitializeAsync();
        await using var context = TestServerFactory.CreateServer($"stale-craft-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Crafter");
        var client = (TestClient)player.Client;

        // The grid changed without its result following it: the result shown, which handlers saw, isn't taken, nor
        // worked out again and taken instead.
        player.Inventory.SetItem(1, new ItemStack(ItemsRegistry.OakLog, 1));
        player.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Diamond, 1));

        await ClickAsync(context.Server, player, 0, 0, ClickType.Pickup);

        Assert.Null(player.CarriedItem);
        Assert.Equal(1, player.Inventory.GetItem(1)?.Count);
        Assert.True(player.Inventory.GetItem(0)?.Is(ItemsRegistry.OakPlanks));
        var shown = client.SentPackets.OfType<ContainerSetContentPacket>().Last();
        Assert.True(shown.Items[0]?.Is(ItemsRegistry.OakPlanks));

        // A change registered with a click is followed by the result too, before the window is sent.
        context.EventDispatcher.RegisterEvent(null, (Func<ContainerClickEventArgs, ValueTask>)(click =>
        {
            click.ApplyWithClick(() => player.Inventory.SetItem(1, null));

            return ValueTask.CompletedTask;
        }), Priority.Normal);

        await ClickAsync(context.Server, player, 9, 0, ClickType.Pickup);

        Assert.Null(player.Inventory.GetItem(0));
        Assert.Null(client.SentPackets.OfType<ContainerSetContentPacket>().Last().Items[0]);
    }

    [Fact(DisplayName = "A number-key swap with the off hand in a container corrects it in the player's inventory",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task OffhandSwapIsCorrectedInThePlayerInventory()
    {
        await using var context = TestServerFactory.CreateServer($"offhand-swap-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Swapper");
        var client = (TestClient)player.Client;
        var chest = await OpenAsync(player, new Container());
        chest.SetItem(0, new ItemStack(ItemsRegistry.Shield));
        var sent = client.SentPackets.Count;

        await ClickAsync(context.Server, player, 0, 40, ClickType.Swap);

        // Vanilla's client sets the off hand from the player inventory packet, at its inventory's index 40.
        var packets = client.SentPackets.Skip(sent).ToList();
        var offhand = Assert.Single(packets.OfType<SetPlayerInventoryPacket>());
        Assert.Equal(40, offhand.Slot);
        Assert.True(offhand.Contents?.Is(ItemsRegistry.Shield));
        Assert.DoesNotContain(packets, packet => packet is ContainerSetSlotPacket);
    }

    [Fact(DisplayName = "Every snapshot of a container's window sets its viewers' off hand",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task ContainerSnapshotsCarryTheOffhand()
    {
        await using var context = TestServerFactory.CreateServer($"offhand-viewer-{Guid.NewGuid():N}");
        var first = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "First");
        var second = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Second");
        var chest = new Container();
        chest.SetItem(0, new ItemStack(ItemsRegistry.Diamond));
        await first.OpenInventoryAsync(chest);
        await second.OpenInventoryAsync(chest);

        // As left by a swap with the off hand whose snapshot is left out for a newer one: another viewer's click.
        first.Inventory.SetItem(45, new ItemStack(ItemsRegistry.Shield));
        var client = (TestClient)first.Client;
        var sent = client.SentPackets.Count;

        await ClickAsync(context.Server, second, 0, 0, ClickType.Pickup);

        var offhand = Assert.Single(client.SentPackets.Skip(sent).OfType<SetPlayerInventoryPacket>());
        Assert.Equal(40, offhand.Slot);
        Assert.True(offhand.Contents?.Is(ItemsRegistry.Shield));
    }

    [Fact(DisplayName = "A click's Item keeps the saved components of the stacks nested in it")]
    public void ClickItemKeepsNestedUnmodeledComponents()
    {
        var customData = new NbtCompound
        {
            new NbtCompound("minecraft:custom_data") { new NbtTag<string>("gem", "ruby") }
        };
        var gem = new ItemStack(ItemsRegistry.Diamond) { UnmodeledComponents = customData };
        var bundle = new ItemStack(ItemsRegistry.Bundle);
        bundle[DataComponentType.BundleContents] = ComponentBuilder.BundleContents with { Value = [gem] };
        var chest = new Container();
        chest.SetItem(0, bundle);

        var click = new ContainerClickEventArgs(new FakePlayer(), server, chest)
        {
            ContainerId = 1,
            ClickedSlot = 0,
            Button = 0,
            StateId = 0,
            ClickType = ClickType.Pickup
        };
        click.Arrive(NetworkBuffer.SnapshotItemStack);

        var contents = click.Item!.GetComponent<SimpleDataComponent<ImmutableArray<ItemStack>>>(
            DataComponentType.BundleContents)!;
        var copy = Assert.Single(contents.Value!);
        Assert.NotSame(gem, copy);
        Assert.NotSame(customData, copy.UnmodeledComponents);
        Assert.Equal(gem, copy);
        customData.Clear();
        Assert.Equal("ruby", Assert.IsType<NbtCompound>(
            copy.UnmodeledComponents!["minecraft:custom_data"]).GetString("gem"));
    }

    [Fact(DisplayName = "A window snapshot isn't sent after a newer one, nor once the window changed",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task ObsoleteSnapshotsAreNotSent()
    {
        await using var context = TestServerFactory.CreateServer($"snapshots-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Viewer");
        var client = (TestClient)player.Client;
        var order = player.WindowSnapshots;
        player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.Stone));
        var sent = client.SentPackets.Count;

        var older = Snapshot(order.Next());
        var newer = Snapshot(order.Next());

        Assert.False(await order.SendAsync(newer));
        Assert.False(await order.SendAsync(older));
        Assert.Same(newer.Packets[0], Assert.Single(client.SentPackets.Skip(sent)));

        // The held stack changed in place since it was captured: a newer snapshot is needed.
        var changed = Snapshot(order.Next());
        player.Inventory.GetItem(36)!.Count++;

        Assert.True(await order.SendAsync(changed));
        Assert.Single(client.SentPackets.Skip(sent));
        Assert.True(order.IsStale);

        WindowSnapshot Snapshot(long sequence) =>
            new(player, sequence, ClickWindowState.Of(player), [new SetPlayerInventoryPacket { Slot = 0, Contents = null }]);
    }

    [Fact(DisplayName = "A picked-up arrow's slot goes through plugins' packet handlers",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task PickupGoesThroughPacketHandlers()
    {
        await using var context = TestServerFactory.CreateServer($"pickup-handlers-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Collector");
        var client = (TestClient)player.Client;
        var handled = new List<IClientboundPacket>();
        player.WindowSnapshots = new WindowSnapshots(Redact, packet => client.SendPacket(packet));
        var sent = client.SentPackets.Count;

        Assert.True(await player.ReceiveProjectileAsync(new ItemStack(ItemsRegistry.Arrow), creativeOnly: false));

        Assert.True(player.Inventory.GetItem(36)?.Is(ItemsRegistry.Arrow));
        Assert.IsType<ContainerSetSlotPacket>(Assert.Single(handled));
        Assert.Empty(client.SentPackets.Skip(sent));

        // Stands in for a plugin's queue handler that cancels the inventory's slot updates.
        ValueTask<bool> Redact(IClientboundPacket packet)
        {
            handled.Add(packet);

            return ValueTask.FromResult(packet is not ContainerSetSlotPacket);
        }
    }

    [Fact(DisplayName = "A window that keeps changing while handlers decide is sent later, through them, as it is",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task StaleWindowIsSentLaterThroughHandlers()
    {
        await using var context = TestServerFactory.CreateServer($"stale-window-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Viewer");
        var client = (TestClient)player.Client;
        player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.Stone));
        var handled = new List<IClientboundPacket>();
        var changing = true;
        player.WindowSnapshots = new WindowSnapshots(Decide, packet => client.SendPacket(packet));
        var sent = client.SentPackets.Count;

        // Each snapshot the click sends meets a change, so none is sent, and the window is left stale.
        await ClickAsync(context.Server, player, 9, 0, ClickType.Pickup);

        Assert.Equal(2, handled.Count);
        Assert.Empty(client.SentPackets.Skip(sent));
        Assert.True(player.WindowSnapshots.IsStale);

        // A tick tries once, not again and again while the window keeps changing.
        await MainEventHandler.RefreshStaleWindowAsync(player);

        Assert.Equal(3, handled.Count);
        Assert.Empty(client.SentPackets.Skip(sent));

        // Once it holds still, the window is sent as it is now, through the handlers.
        changing = false;
        await MainEventHandler.RefreshStaleWindowAsync(player);

        var shown = Assert.IsType<ContainerSetContentPacket>(Assert.Single(client.SentPackets.Skip(sent)));
        Assert.Contains(shown, handled);
        Assert.Equal(4, shown.Items[36]?.Count);
        Assert.False(player.WindowSnapshots.IsStale);

        // Stands in for a plugin's queue handler during which the held stack grows, as a pickup would grow it.
        ValueTask<bool> Decide(IClientboundPacket packet)
        {
            handled.Add(packet);

            if (changing)
                player.Inventory.GetItem(36)!.Count++;

            return ValueTask.FromResult(true);
        }
    }

    [Fact(DisplayName = "A picked-up item's animation and slots each go through plugins' packet handlers",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task ItemPickupPacketsEachGoThroughPacketHandlers()
    {
        await using var context = TestServerFactory.CreateServer($"item-pickup-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Collector");
        var client = (TestClient)player.Client;
        var handled = new List<IClientboundPacket>();
        player.WindowSnapshots = new WindowSnapshots(Redact, packet => client.SendPacket(packet));
        var item = new Obsidian.Entities.ItemEntity
        {
            Level = context.World,
            EntityId = Server.GetNextEntityId(),
            Position = player.Position,
            Item = new ItemStack(ItemsRegistry.Diamond, 3),
            CanPickup = true
        };
        Assert.True(context.World.TryAddEntity(item));
        var sent = client.SentPackets.Count;

        await player.PickupNearbyItemsAsync();

        Assert.Equal(3, player.Inventory.GetItem(36)?.Count);
        Assert.Collection(
            handled,
            packet => Assert.IsType<TakeItemEntityPacket>(packet),
            packet => Assert.IsType<SetPlayerInventoryPacket>(packet));

        // Only the animation is left, so it isn't bundled.
        Assert.IsType<TakeItemEntityPacket>(Assert.Single(client.SentPackets.Skip(sent)));

        // Stands in for a plugin's queue handler that cancels the inventory's slot updates.
        ValueTask<bool> Redact(IClientboundPacket packet)
        {
            handled.Add(packet);

            return ValueTask.FromResult(packet is not SetPlayerInventoryPacket);
        }
    }

    [Fact(DisplayName = "A merchant's window sent after a stale pickup keeps its state id, so the next click applies",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task StaleMerchantWindowKeepsItsStateId()
    {
        await using var context = TestServerFactory.CreateServer($"stale-merchant-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Customer");
        var client = (TestClient)player.Client;
        player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.Emerald, 5));
        var villager = new Obsidian.Entities.Villager
        {
            Level = context.World,
            EntityId = Server.GetNextEntityId(),
            Position = player.Position,
            Health = 20
        };
        var offer = new TradeEntry
        {
            FirstInput = new TradeItem(ItemsRegistry.Emerald.Id, 1, []),
            SecondInput = new TradeItem(0, 0, []),
            Output = new ItemStack(ItemsRegistry.Bread),
            MaxCount = 12
        };
        await Obsidian.Entities.MerchantTrading.OpenAsync(villager, player, [offer], 1, 0, canRestock: true);
        Assert.IsType<Obsidian.Entities.MerchantContainer>(player.OpenedContainer);
        var stateId = client.SentPackets.OfType<ContainerSetContentPacket>().Last().StateId;

        // The emeralds grow while handlers decide on a picked-up arrow's slot, which is then left out.
        var changing = true;
        player.WindowSnapshots = new WindowSnapshots(Decide, packet => client.SendPacket(packet));
        Assert.True(await player.ReceiveProjectileAsync(new ItemStack(ItemsRegistry.Arrow), creativeOnly: false));
        Assert.True(player.WindowSnapshots.IsStale);

        changing = false;
        var sent = client.SentPackets.Count;
        await MainEventHandler.RefreshStaleWindowAsync(player);

        var shown = client.SentPackets.Skip(sent).OfType<ContainerSetContentPacket>().Single();
        Assert.Equal(6, shown.Items[3]?.Count);
        Assert.Equal(stateId, shown.StateId);
        Assert.False(player.WindowSnapshots.IsStale);

        // The client clicks with the id it was sent last: the click picks the emeralds up.
        await ClickAsync(context.Server, player, 3, 0, ClickType.Pickup, shown.StateId);

        Assert.Equal(6, player.CarriedItem?.Count);
        Assert.Null(player.Inventory.GetItem(9));

        ValueTask<bool> Decide(IClientboundPacket packet)
        {
            if (changing)
                player.Inventory.GetItem(9)!.Count++;

            return ValueTask.FromResult(true);
        }
    }

    private static async Task<T> OpenAsync<T>(Obsidian.Entities.Player player, T container) where T : BaseContainer
    {
        await player.OpenInventoryAsync(container);

        return container;
    }

    // Clicks a slot of the player's open window through the click packet, carrying the given state id.
    private static async Task ClickAsync(
        Server server,
        Obsidian.Entities.Player player,
        short slot,
        sbyte button,
        ClickType type,
        int stateId = 0)
    {
        var containerId = player.OpenedContainer is null ? 0 : player.CurrentContainerId;

        await Packet(containerId, slot, button, type, stateId).HandleAsync(server, player);
    }

    private static SetCreativeModeSlotPacket CreativeSlot(short slot, ItemStack item)
    {
        using var writer = new NetworkBuffer();
        writer.WriteShort(slot);
        writer.WriteItemStack(item, delimitedComponents: true);

        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
        var packet = new SetCreativeModeSlotPacket();
        packet.Populate(reader);

        return packet;
    }

    // Clicks a slot as the window numbers it, found in the menu or the player's inventory like the click packet does.
    private static void Click(FakePlayer player, short windowSlot, sbyte button, ClickType type)
    {
        var (slot, forPlayer) = player.OpenedContainer.GetSlot(windowSlot);
        var container = forPlayer ? player.Inventory : player.OpenedContainer;

        MainEventHandler.OnInventoryClickTest(new ContainerClickEventArgs(player, server, container)
        {
            ContainerId = 1,
            ClickedSlot = slot,
            WindowSlot = windowSlot,
            Button = button,
            StateId = 0,
            ClickType = type
        });
    }

    private static ContainerClickPacket Packet(
        int containerId,
        short slot,
        sbyte button,
        ClickType type,
        int stateId = 0)
    {
        using var writer = new NetworkBuffer();
        writer.WriteVarInt(containerId);
        writer.WriteVarInt(stateId);
        writer.WriteShort(slot);
        writer.WriteByte(button);
        writer.WriteVarInt((int)type);
        writer.WriteVarInt(0);
        writer.WriteBoolean(false);

        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
        var packet = new ContainerClickPacket();
        packet.Populate(reader);

        return packet;
    }
}
