using System.Collections.Immutable;
using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Containers;
using Obsidian.API.Data;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.Events;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
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
    public void DragMovesItemsWithoutSurvivalDuplication()
    {
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
    }

    [Fact]
    public void MenuTransfersPreserveStacksAndComponents()
    {
        {
            var menu = new MenuContainer(pouch, "Pouch");
            var player = new FakePlayer { OpenedContainer = menu };
            var named = new ItemStack(
                ItemsRegistry.Stone, 5, ComponentBuilder.CustomName with { Value = "Named stone" });
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
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task SharedChestClicksDoNotDuplicateItems()
    {
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

        {
            await using var context = TestServerFactory.CreateServer($"chest-open-{Guid.NewGuid():N}");
            Obsidian.Entities.Player[] players =
            [
                TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "First"),
                TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Second")
            ];
            var position = new Vector(0, 64, 0);

            // A chest without its block entity yet, as in a world saved before chests got theirs when placed.
            var chunk = new Chunk(0, 0);
            chunk.SetChunkStatus(ChunkGenStage.full);
            chunk.SetBlock(position.X, position.Y, position.Z, BlocksRegistry.Chest);
            context.World.LoadRegionByChunk(0, 0).SetChunk(chunk);

            await Task.WhenAll(players.Select(player => Task.Run(async () =>
                await context.Server.EventDispatcher.ExecuteEventAsync(
                    new PlayerInteractEventArgs(player, context.Server)
                    {
                        Block = BlocksRegistry.Chest,
                        BlockLocation = position
                    }))));

            var blockEntity = await context.World.GetBlockEntityAsync(position);
            var stored = Assert.IsAssignableFrom<ContainerBlockEntity>(blockEntity).Inventory;
            Assert.All(players, player => Assert.Same(stored, player.OpenedContainer));
            Assert.Equal(2, stored.Viewers.Count);
        }

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

            var leftHalf = await HalfAsync(new Vector(0, 64, 0));
            var rightHalf = await HalfAsync(new Vector(1, 64, 0));
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

            Assert.Equal(3, leftHalf.GetItem(0)?.Count);
            var shown = ((TestClient)second.Client).SentPackets.OfType<ContainerSetContentPacket>().Last();
            Assert.Equal(second.CurrentContainerId, shown.ContainerId);
            Assert.Equal(3, shown.Items[27]?.Count);

            async Task InteractAsync(Obsidian.Entities.Player player, IBlock block, Vector position) =>
                await context.Server.EventDispatcher.ExecuteEventAsync(
                    new PlayerInteractEventArgs(player, context.Server)
                    {
                        Block = block,
                        BlockLocation = position
                    });

            async Task<BaseContainer> HalfAsync(Vector position) =>
                Assert.IsAssignableFrom<ContainerBlockEntity>(
                    await context.World.GetBlockEntityAsync(position)).Inventory;
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

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task StaleClicksCannotDuplicateItems()
    {
        {
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
                        // Another change lands while the handler decides. Its registered click change mustn't run.
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
                Assert.True(
                    client.SentPackets.OfType<ContainerSetContentPacket>().Last().Items[0]?.Is(ItemsRegistry.Stone));

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
                    Obsidian.Entities.Player.AddToInventory(
                        player.Inventory, 36, new ItemStack(ItemsRegistry.Diamond, 1));
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

            {
                await RecipesRegistry.InitializeAsync();
                await using var context = TestServerFactory.CreateServer($"stale-craft-{Guid.NewGuid():N}");
                var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Crafter");
                var client = (TestClient)player.Client;

                // If the grid changes before its result follows, the stale result must not be taken or recomputed
                // and taken instead.
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
        }

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

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task WindowSnapshotsRecoverWithoutSendingStaleState()
    {
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
                new(player, sequence, ClickWindowState.Of(player), [new SetPlayerInventoryPacket
                {
                    Slot = 0,
                    Contents = null
                }]);
        }

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

    [Fact(DisplayName = "A plugin's menu keeps slots elsewhere under its rules, syncs data and closes with its block",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task BoundMenusFollowTheirSlotsAndBlock()
    {
        await using var context = TestServerFactory.CreateServer($"bound-menu-{Guid.NewGuid():N}");
        var chunk = new Chunk(0, 0);
        chunk.SetChunkStatus(ChunkGenStage.full);
        context.World.LoadRegionByChunk(0, 0).SetChunk(chunk);

        var position = new Vector(0, 64, 0);
        await context.World.SetBlockAsync(position, BlocksRegistry.Chest);
        var chest = Assert.IsAssignableFrom<ContainerBlockEntity>(await context.World.GetBlockEntityAsync(position));
        var player = TestServerFactory.CreateConnectedPlayer(
            context.Server, context.World, "Machinist", new VectorD(0.5, 64, 2.5));
        var sent = ((TestClient)player.Client).SentPackets;

        // Fuel kept in the chest, sixteen at most; a buffer behind item access; an output players only take from.
        var buffer = new BufferAccess();
        var menu = new MachineMenu(machine, chest, buffer);
        await player.OpenInventoryAsync(menu);
        Assert.Contains(player, chest.Inventory.Viewers);

        player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.Coal, 20));
        player.Inventory.SetItem(10, new ItemStack(ItemsRegistry.Diamond, 5));
        await ClickAsync(context.Server, player, 3, 0, ClickType.QuickMove);
        await ClickAsync(context.Server, player, 4, 0, ClickType.QuickMove);

        Assert.Equal(16, chest.Inventory.GetItem(0)?.Count);
        Assert.Equal(4, player.Inventory.GetItem(9)?.Count);
        Assert.Equal(5, buffer.Stack?.Count);
        Assert.Null(player.Inventory.GetItem(10));

        // A commit callback that throws before transferring must undo the cursor's predicted pickup too.
        buffer.ThrowExtract = true;
        await ClickAsync(context.Server, player, 1, 0, ClickType.Pickup);
        Assert.Null(player.CarriedItem);
        Assert.Equal(5, buffer.Stack?.Count);
        buffer.ThrowExtract = false;

        // A click whose buffer lost items before the click ended is undone, so that no item is made.
        buffer.Steal = true;
        await ClickAsync(context.Server, player, 1, 0, ClickType.Pickup);
        Assert.Null(player.CarriedItem);
        Assert.Equal(4, buffer.Stack?.Count);

        buffer.Steal = false;
        await ClickAsync(context.Server, player, 1, 0, ClickType.Pickup);
        await ClickAsync(context.Server, player, 2, 0, ClickType.Pickup);
        Assert.Equal(4, player.CarriedItem?.Count);
        Assert.Null(menu.GetItem(2));

        // Data slots are sent as the menu opens, then on the player's ticks only when they changed; buttons reach the
        // menu.
        await player.TickMenuAsync();
        var data = sent.OfType<ContainerSetDataPacket>().Select(packet => packet.WindowProperty.Value).ToList();
        Assert.Equal([0, 16], data);

        await player.TickMenuAsync();
        Assert.Equal(2, sent.OfType<ContainerSetDataPacket>().Count());

        await player.ClickMenuButtonAsync(player.CurrentContainerId, 0);
        Assert.True(menu.GetItem(2)?.Is(ItemsRegistry.Emerald));

        // Out of reach the menu closes, and with its block gone it closes too.
        player.Position = new VectorD(0.5, 64, 12.5);
        await player.TickMenuAsync();
        Assert.Null(player.OpenedContainer);

        player.Position = new VectorD(0.5, 64, 2.5);
        await player.OpenInventoryAsync(new MachineMenu(machine, chest, buffer));
        await context.World.SetBlockAsync(position, BlocksRegistry.Air);
        Assert.Null(player.OpenedContainer);

        var first = new BufferAccess { Stack = new ItemStack(ItemsRegistry.Diamond, 3) };
        var second = new BufferAccess
        {
            Stack = new ItemStack(ItemsRegistry.Diamond, 5),
            ThrowExtract = true
        };
        var rollback = new MenuContainer(machine, "Rollback")
        {
            Slots = [new MenuSlot(first, 0), new MenuSlot(second, 0), new MenuSlot()]
        };
        await player.OpenInventoryAsync(rollback);

        // Earlier successful extractions are journalled. If returning one throws, it goes to the inventory instead.
        foreach (var refuseReturn in new[] { false, true })
        {
            first.ThrowInsert = refuseReturn;
            using (await BaseContainer.LockWindowAsync(rollback, player.Inventory))
            {
                using var click = MainEventHandler.MenuClick.Start(player, rollback)!;
                rollback.SetItem(0, null);
                rollback.SetItem(1, null);
                player.CarriedItem = new ItemStack(ItemsRegistry.Diamond, 12);
                Assert.Throws<InvalidOperationException>(() => click.Commit());
            }

            Assert.Equal(4, player.CarriedItem?.Count);
            Assert.Equal(refuseReturn ? null : 3, first.Stack?.Count);
            Assert.Equal(5, second.Stack?.Count);
            Assert.Equal(refuseReturn ? 3 : 0, player.Inventory.Sum(item => item?.Is(ItemsRegistry.Diamond) == true
                ? item.Count
                : 0));
        }

        // A later insertion failure also reverses earlier insertions. If the access refuses that extraction, the
        // restored player stacks omit the items still in the access, so rollback cannot duplicate them.
        first.ThrowInsert = false;
        second.ThrowInsert = true;
        foreach (var refuseRecovery in new[] { false, true })
        {
            first.ThrowExtract = refuseRecovery;
            player.CarriedItem = new ItemStack(ItemsRegistry.Diamond, 4);
            using (await BaseContainer.LockWindowAsync(rollback, player.Inventory))
            {
                using var click = MainEventHandler.MenuClick.Start(player, rollback)!;
                rollback.SetItem(0, new ItemStack(ItemsRegistry.Diamond, 2));
                rollback.SetItem(1, new ItemStack(ItemsRegistry.Diamond, 7));
                player.CarriedItem = null;
                Assert.Throws<InvalidOperationException>(() => click.Commit());
            }

            var restoredDiamonds = (player.CarriedItem?.Count ?? 0)
                + player.Inventory.Sum(item => item?.Is(ItemsRegistry.Diamond) == true ? item.Count : 0);
            Assert.Equal(refuseRecovery ? 5 : 7, restoredDiamonds);
            Assert.Equal(refuseRecovery ? 2 : null, first.Stack?.Count);
            Assert.Equal(5, second.Stack?.Count);
        }

        // Finished player drops stay host-owned even while the menu's plugin call is active.
        var owner = new PluginContainer
        {
            Info = new PluginInfo("dropowner"),
            Archive = PluginArchive.Open("dropowner.obby", PluginArchives.Zip(new Dictionary<string, byte[]>
            {
                [PluginMetadata.FileName] = PluginArchives.Metadata("dropowner", "1.0.0", "Drop owner")
            }))
        };
        using (owner.TryEnter())
        using (await BaseContainer.LockWindowAsync(rollback, player.Inventory))
        {
            using var click = MainEventHandler.MenuClick.Start(player, rollback)!;
            click.Drop(new ItemStack(ItemsRegistry.Emerald, 2));
            Assert.True(click.Commit());
        }

        // The chest's coal lies nearby too, in one or two item entities as vanilla splits it at random.
        var dropped = Assert.Single(context.World.GetNonPlayerEntitiesInRange(player.Position, 4)
            .OfType<Obsidian.Entities.ItemEntity>()
            .Where(item => item.Item.Is(ItemsRegistry.Emerald)));
        Assert.Equal(2, dropped.Item.Count);
        Assert.Null(dropped.SpawnedBy);
        owner.Close();
        Obsidian.Plugins.PluginEntities.RemoveFrom(context.World, owner);
        Assert.Contains(dropped, context.World.GetNonPlayerEntitiesInRange(player.Position, 4));
    }

    [Fact(DisplayName = "A player's item attachment backs menu slots, saves, follows death rules and outlives unload",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task ItemAttachmentsBackPlayerSlots()
    {
        await using var context = TestServerFactory.CreateServer($"player-slots-{Guid.NewGuid():N}");
        var chunk = new Chunk(0, 0);
        chunk.SetChunkStatus(ChunkGenStage.full);
        context.World.LoadRegionByChunk(0, 0).SetChunk(chunk);
        var player = TestServerFactory.CreateConnectedPlayer(
            context.Server, context.World, "Wearer", new VectorD(0.5, 64, 0.5));
        var owner = new PluginContainer
        {
            Info = new PluginInfo("charms"),
            Archive = PluginArchive.Open("charms.obby", PluginArchives.Zip(new Dictionary<string, byte[]>
            {
                [PluginMetadata.FileName] = PluginArchives.Metadata("charms", "1.0.0", "Charms")
            }))
        };

        try
        {
            var charms = PluginRegistry.RegisterAttachment(
                owner, "charms:worn", ItemAttachments.Codec, ItemAttachments.NetworkCodec, copyOnDeath: false);
            var kept = PluginRegistry.RegisterAttachment(
                owner, "charms:soulbound", ItemAttachments.Codec, null, copyOnDeath: true);
            var changes = 0;
            charms.Changed += _ => changes++;

            // Two charm slots that take one diamond each, next to the player's inventory.
            var access = player.GetItemAccess(charms, 2);
            var menu = new MenuContainer(charmMenu, "Charms")
            {
                Slots =
                [
                    .. Enumerable.Range(0, 2).Select(slot => new MenuSlot(access, slot)
                    {
                        MayPlace = item => item.Is(ItemsRegistry.Diamond),
                        MaxStackSize = 1
                    })
                ]
            };
            await player.OpenInventoryAsync(menu);
            player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.Diamond, 3));
            player.Inventory.SetItem(10, new ItemStack(ItemsRegistry.Coal, 1));

            // Shift-clicks follow the slots' rules into the attachment, and back to the inventory.
            await ClickAsync(context.Server, player, 2, 0, ClickType.QuickMove);
            await ClickAsync(context.Server, player, 3, 0, ClickType.QuickMove);
            Assert.Equal(1, player.Inventory.GetItem(9)?.Count);
            Assert.True(player.Inventory.GetItem(10)?.Is(ItemsRegistry.Coal));
            Assert.All(player.GetAttachment(charms)!, item => Assert.Equal(1, item?.Count));

            await ClickAsync(context.Server, player, 0, 0, ClickType.QuickMove);
            Assert.Null(menu.GetItem(0));
            Assert.Equal(2, player.Inventory.GetItem(9)?.Count);
            Assert.Equal(2, changes); // One notification per changed holder and type per completed click.

            // The attachment is the only copy: saved and loaded as NBT.
            var saved = new NbtCompound();
            AttachmentStorage.For(player).Save(saved);
            var reloaded = new Obsidian.Entities.Entity { Level = context.World, Type = EntityType.Pig };
            AttachmentStorage.For(reloaded).Load(saved);
            Assert.True(reloaded.GetAttachment(charms)![1]?.Is(ItemsRegistry.Diamond));

            // keep_inventory keeps the items and experience; without it a death drops them like vanilla, the
            // attachments' too unless the type copies on death. Either way the death closes the menu, so that no
            // click moves the items it drops.
            player.SetAttachment(kept, [new ItemStack(ItemsRegistry.Emerald, 4)]);
            player.XpLevel = 20;
            player.XpTotal = 550;
            context.World.LevelData.GameRules["keep_inventory"] = "true";
            await player.KillAsync(player, "Kept");
            Assert.Null(player.OpenedContainer);
            await player.RespawnAsync();
            Assert.NotNull(player.GetAttachment(charms));
            Assert.Equal(2, player.Inventory.GetItem(9)?.Count);
            Assert.Equal(20, player.XpLevel);

            var vanishing = new ItemStack(ItemsRegistry.Stick, 1);
            EnchantmentHelper.Enchant(vanishing, EnchantmentsRegistry.VanishingCurse, 1);
            player.Inventory.SetItem(45, vanishing);
            player.Inventory.SetItem(6, new ItemStack(ItemsRegistry.IronChestplate, 1));
            context.World.LevelData.GameRules["keep_inventory"] = "false";
            changes = 0;
            await player.KillAsync(player, "Dropped");
            Assert.False(player.TryGetAttachment(charms, out _));
            Assert.Equal(1, changes);
            Assert.All(player.Inventory, Assert.Null);
            var nearby = context.World.GetNonPlayerEntitiesInRange(player.Position, 4).ToList();
            var drops = nearby.OfType<Obsidian.Entities.ItemEntity>().Select(item => item.Item).ToList();
            Assert.Contains(drops, item => item.Is(ItemsRegistry.Diamond));
            Assert.Contains(drops, item => item.Is(ItemsRegistry.Coal));
            Assert.Contains(drops, item => item.Is(ItemsRegistry.IronChestplate));
            Assert.DoesNotContain(drops, item => item.Is(ItemsRegistry.Stick));

            // Vanilla's min(level * 7, 100) points, in its orb sizes.
            var orbs = nearby.OfType<Obsidian.Entities.ExperienceOrb>().Select(orb => orb.Value);
            Assert.Equal([3, 7, 17, 73], orbs.Order());

            await player.RespawnAsync();
            Assert.Equal(4, Assert.Single(player.GetAttachment(kept)!)?.Count);
            Assert.Equal((0, 0), (player.XpLevel, player.XpTotal));
            Assert.All(((TestClient)player.Client).SentPackets.OfType<ContainerSetContentPacket>().Last().Items,
                Assert.Null);

            // Death waits for the window transaction. Transient inputs and cursor drop even with keep_inventory,
            // whether the close notification is cancelled or its first handler throws; the result is never dropped.
            var rejectClose = false;
            context.EventDispatcher.RegisterEvent(null, (Func<ContainerClosedEventArgs, ValueTask>)(closed =>
            {
                if (!ReferenceEquals(closed.Player, player))
                    return ValueTask.CompletedTask;

                if (rejectClose)
                    closed.Cancel();
                else
                    throw new InvalidOperationException("Failed close notification");

                return ValueTask.CompletedTask;
            }));
            context.World.LevelData.GameRules["keep_inventory"] = "true";

            for (var attempt = 0; attempt < 2; attempt++)
            {
                rejectClose = attempt == 1;
                var table = new CraftingTable();
                await player.OpenInventoryAsync(table);
                table.SetItem(0, new ItemStack(ItemsRegistry.Diamond, 1));
                table.SetItem(1, new ItemStack(ItemsRegistry.GoldIngot, 5));
                player.CarriedItem = new ItemStack(ItemsRegistry.CopperIngot, 2);
                player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.Emerald, 3));
                var before = context.World.GetNonPlayerEntitiesInRange(player.Position, 8)
                    .OfType<Obsidian.Entities.ItemEntity>()
                    .Select(entity => entity.EntityId)
                    .ToHashSet();
                Task dying;

                using (await BaseContainer.LockWindowAsync(table, player.Inventory))
                {
                    dying = player.KillAsync(player, "Locked crafting death").AsTask();
                    Assert.False(dying.IsCompleted);
                    Assert.Equal(5, table.GetItem(1)?.Count);
                    Assert.Equal(2, player.CarriedItem?.Count);
                    Assert.Equal(3, player.Inventory.GetItem(9)?.Count);
                }

                await dying.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Null(player.OpenedContainer);
                Assert.All(table, Assert.Null);
                Assert.Null(player.CarriedItem);
                Assert.Equal(3, player.Inventory.GetItem(9)?.Count);
                var transient = context.World.GetNonPlayerEntitiesInRange(player.Position, 8)
                    .OfType<Obsidian.Entities.ItemEntity>()
                    .Where(entity => !before.Contains(entity.EntityId))
                    .Select(entity => entity.Item)
                    .ToArray();
                Assert.Equal(2, transient.Length);
                Assert.Contains(transient, item => item.Is(ItemsRegistry.GoldIngot) && item.Count == 5);
                Assert.Contains(transient, item => item.Is(ItemsRegistry.CopperIngot) && item.Count == 2);
                await player.RespawnAsync();
            }

            // Another player's attachment is swapped completely before Changed runs. The callback can spawn items
            // and kill its holder, without click locks, a global transfer lock, or a half-swapped death drain.
            var viewer = TestServerFactory.CreateConnectedPlayer(
                context.Server, context.World, "Viewer", player.Position);
            context.World.LevelData.GameRules["keep_inventory"] = "false";
            player.SetAttachment(charms, [new ItemStack(ItemsRegistry.Diamond, 1)]);
            var pauseClick = true;
            var clickStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var resumeClick = new System.Threading.ManualResetEventSlim();
            var otherMenu = new MenuContainer(charmMenu, "Other player's slots")
            {
                Slots =
                [
                    new MenuSlot(access, 0)
                    {
                        MayPickup = _ =>
                        {
                            if (pauseClick)
                            {
                                clickStarted.TrySetResult();
                                Assert.True(resumeClick.Wait(TimeSpan.FromSeconds(10)));
                            }

                            return true;
                        }
                    },
                    new MenuSlot(access, 1)
                ]
            };
            await viewer.OpenInventoryAsync(otherMenu);
            viewer.CarriedItem = new ItemStack(ItemsRegistry.Dirt, 1);

            // A concurrent death also waits for an already-started transfer in somebody else's window.
            var clicking = Task.Run(() => ClickAsync(context.Server, viewer, 0, 0, ClickType.Pickup));
            Task holderDying;
            try
            {
                await clickStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                holderDying = player.KillAsync(player, "Concurrent attachment death").AsTask();
                Assert.False(holderDying.IsCompleted);
                Assert.True(access.GetItem(0)?.Is(ItemsRegistry.Diamond));
            }
            finally
            {
                resumeClick.Set();
            }

            await Task.WhenAll(clicking, holderDying).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(viewer.CarriedItem?.Is(ItemsRegistry.Diamond));
            Assert.False(player.TryGetAttachment(charms, out _));
            await player.RespawnAsync();

            pauseClick = false;
            player.SetAttachment(charms, [new ItemStack(ItemsRegistry.Diamond, 1)]);
            viewer.CarriedItem = new ItemStack(ItemsRegistry.Dirt, 1);
            var unlocked = false;
            charms.Changed += holder =>
            {
                if (!ReferenceEquals(holder, player) || !player.Alive)
                    return;

                var inventoryFree = viewer.Inventory.TransactionLock.Wait(0);
                if (inventoryFree)
                    viewer.Inventory.TransactionLock.Release();

                var windowFree = otherMenu.TransactionLock.Wait(0);
                if (windowFree)
                    otherMenu.TransactionLock.Release();

                unlocked = inventoryFree && windowFree;

                context.World.SpawnItem(player.Position, new ItemStack(ItemsRegistry.Stick, 1));
                _ = player.KillAsync(player, "Attachment callback death");
            };
            await ClickAsync(context.Server, viewer, 0, 0, ClickType.Pickup);
            Assert.True(unlocked);
            Assert.False(player.Alive);
            Assert.True(viewer.CarriedItem?.Is(ItemsRegistry.Diamond));
            Assert.False(player.TryGetAttachment(charms, out _));
            Assert.Contains(context.World.GetNonPlayerEntitiesInRange(player.Position, 8)
                .OfType<Obsidian.Entities.ItemEntity>(), entity => entity.Item.Is(ItemsRegistry.Dirt));
            await player.RespawnAsync();
            Assert.False(player.TryGetAttachment(charms, out _));

            // Unloaded, the access takes and gives nothing, and the saved items wait for the plugin to return.
            owner.Close();
            owner.UndoHostAdditions();
            var emerald = new ItemStack(ItemsRegistry.Emerald, 1);
            Assert.Null(access.GetItem(0));
            Assert.Same(emerald, access.Insert(0, emerald));
            Assert.True(AttachmentStorage.For(player).Snapshot().HasTag(kept.Identifier));
        }
        finally
        {
            owner.Close();
            owner.UndoHostAdditions();
            await owner.DisposeAsync();
        }
    }

    private static readonly MenuDefinition charmMenu = MenuTypesRegistry.Register("clicks:charms", 1, 2);

    private static readonly MenuDefinition machine = MenuTypesRegistry.Register("clicks:machine", 1, 3);

    // A machine's menu: its fuel in a chest, a buffer behind item access, and an output a button fills.
    private sealed class MachineMenu : MenuContainer
    {
        public MachineMenu(MenuDefinition menu, ContainerBlockEntity chest, IItemAccess buffer) : base(menu, "Machine")
        {
            this.Slots =
            [
                new MenuSlot(chest.Inventory, 0)
                {
                    MayPlace = item => item.Is(ItemsRegistry.Coal),
                    MaxStackSize = 16
                },
                new MenuSlot(buffer, 0) { MayPlace = item => item.Is(ItemsRegistry.Diamond) },
                new MenuSlot { MayPlace = _ => false }
            ];
            this.BlockEntity = chest;
            this.DataSlots = 1;
        }

        public override short GetData(int index) => (short)(this.GetItem(0)?.Count ?? 0);

        protected internal override bool ClickButton(IPlayer player, int button)
        {
            this.SetItem(2, new ItemStack(ItemsRegistry.Emerald, 1));

            return true;
        }
    }

    // One slot of items behind item access, which can lose an item to something else just before one is taken out.
    private sealed class BufferAccess : IItemAccess
    {
        public ItemStack? Stack { get; set; }

        public bool Steal { get; set; }

        public bool ThrowExtract { get; set; }

        public bool ThrowInsert { get; set; }

        public int SlotCount => 1;

        public ItemStack? GetItem(int slot) =>
            this.Stack is ItemStack stack ? new ItemStack(stack, stack.Count) : null;

        public ItemStack? Insert(int slot, ItemStack stack, bool simulate = false)
        {
            if (this.ThrowInsert)
                throw new InvalidOperationException("Insertion failed before transferring.");

            if (this.Stack is not null && !this.Stack.Equals(stack))
                return stack;

            if (!simulate)
                this.Stack = new ItemStack(stack, stack.Count + (this.Stack?.Count ?? 0));

            return null;
        }

        public ItemStack? Extract(int slot, int count, bool simulate = false)
        {
            if (this.ThrowExtract)
                throw new InvalidOperationException("Extraction failed before transferring.");

            if (this.Steal && this.Stack is not null)
                this.Stack.Count--;

            if (this.Stack is not ItemStack stack)
                return null;

            var taken = Math.Min(count, stack.Count);
            if (!simulate)
                this.Stack = stack.Count > taken ? new ItemStack(stack, stack.Count - taken) : null;

            return new ItemStack(stack, taken);
        }
    }

    private static async Task<T> OpenAsync<T>(Obsidian.Entities.Player player, T container) where T : BaseContainer
    {
        await player.OpenInventoryAsync(container);

        return container;
    }

    // Clicks a slot of the player's open window through the click packet, carrying the given state id.
    internal static async Task ClickAsync(
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
