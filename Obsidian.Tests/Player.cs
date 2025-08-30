using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Events;
using Obsidian.Tests.Fakes;
using System.Linq;
using Xunit;

namespace Obsidian.Tests;
public sealed class Player
{
    private readonly MainEventHandler _handler = new(null);

    private static readonly FakeServer server = new();

    [Fact]
    public void Pickup_ShouldMoveItemToCarried()
    {
        var player = new FakePlayer();
        var container = new Container(9); // ✅ divisible by 9
        var item = new ItemStack(ItemsRegistry.Stone, 5);
        container.SetItem(0, item);

        var args = ContainerClickEventArgs.Create(
            player,
            server,
            container,
            containerId: 1,
            clickedSlot: 0,
            button: 0,
            stateId: 0,
            clickType: ClickType.Pickup
        );

        _handler.OnInventoryClickTest(args);

        Assert.Equal(item, player.CarriedItem);
        Assert.Null(container.GetItem(0));
    }

    [Fact]
    public void QuickMove_ShouldMoveItemToPlayerInventory()
    {
        var player = new FakePlayer();
        var container = new Container(9);
        var item = new ItemStack(ItemsRegistry.Dirt, 10);
        container.SetItem(0, item);

        var args = ContainerClickEventArgs.Create(
            player,
            server,
            container,
            containerId: 1, // non-zero = container
            clickedSlot: 0,
            button: 0,
            stateId: 0,
            clickType: ClickType.QuickMove
        );

        _handler.OnInventoryClickTest(args);

        Assert.Null(container.GetItem(0));
        Assert.Contains(Enumerable.Range(0, player.Inventory.Size).Select(i =>
        player.Inventory.GetItem((short)i)), i => i == item);
    }

    [Fact]
    public void Clone_ShouldGiveMaxStackInCreative()
    {
        var player = new FakePlayer { Gamemode = Gamemode.Creative };
        var container = new Container(9);
        var item = new ItemStack(ItemsRegistry.Diamond, 1);
        container.SetItem(0, item);

        var args = ContainerClickEventArgs.Create(
            player,
            server,
            container,
            containerId: 1,
            clickedSlot: 0,
            button: 2, // middle click
            stateId: 0,
            clickType: ClickType.Clone
        );

        _handler.OnInventoryClickTest(args);

        Assert.NotNull(player.CarriedItem);
        Assert.Equal(player.CarriedItem.MaxStackSize, player.CarriedItem.Count);
    }

    [Fact]
    public void Throw_ShouldRemoveItemFromSlot()
    {
        var player = new FakePlayer();
        var container = new Container(9);
        var item = new ItemStack(ItemsRegistry.Apple, 5);
        container.SetItem(0, item);

        var args = ContainerClickEventArgs.Create(
            player,
            server,
            container,
            containerId: 1,
            clickedSlot: 0,
            button: 0, // drop one
            stateId: 0,
            clickType: ClickType.Throw
        );

        _handler.OnInventoryClickTest(args);

        var remaining = container.GetItem(0);
        Assert.True(remaining == null || remaining.Count < 5);
    }

    [Fact]
    public void PickupAll_ShouldMergeStacks()
    {
        var player = new FakePlayer();

        var container = new Container(9);
        var carried = new ItemStack(ItemsRegistry.OakPlanks, 10);
        var stack1 = new ItemStack(ItemsRegistry.OakPlanks, 20);
        var stack2 = new ItemStack(ItemsRegistry.OakPlanks, 15);

        player.CarriedItem = carried;
        container.SetItem(0, stack1);
        container.SetItem(1, stack2);

        var args = ContainerClickEventArgs.Create(
            player,
            server,
            container,
            containerId: 1,
            clickedSlot: 0,
            button: 0,
            stateId: 0,
            clickType: ClickType.PickupAll
        );

        _handler.OnInventoryClickTest(args);

        Assert.True(player.CarriedItem.Count > 10);
        Assert.True(container.GetItem(0) == null || container.GetItem(0).Count < 20);
    }
}
