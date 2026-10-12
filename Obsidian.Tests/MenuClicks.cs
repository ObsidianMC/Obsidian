using Obsidian.API;
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
/// Clicks in a plugin's menu, which numbers its own slots then the player's inventory like vanilla's menus.
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
