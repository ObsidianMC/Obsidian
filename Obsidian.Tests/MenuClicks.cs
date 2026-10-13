using Obsidian.API;
using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Events;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using System;
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

    [Fact(DisplayName = "Viewers of a shared menu take turns and see each other's clicks", Timeout = 10000)]
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

    [Fact(DisplayName = "Drags in the player's inventory follow vanilla's drag order and slot rules", Timeout = 10000)]
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
        Timeout = 10000)]
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

    [Fact(DisplayName = "Players opening a new chest at once share its one stored container", Timeout = 10000)]
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

    private static ContainerClickPacket Packet(int containerId, short slot, sbyte button, ClickType type)
    {
        using var writer = new NetworkBuffer();
        writer.WriteVarInt(containerId);
        writer.WriteVarInt(0);
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
