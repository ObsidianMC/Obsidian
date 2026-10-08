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
    private static readonly FakeServer server = new();

    [Fact]
    public void Pickup_ShouldMoveItemToCarried()
    {
        var player = new FakePlayer();
        var container = new Container(9);
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

        MainEventHandler.OnInventoryClickTest(args);

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
            containerId: 1,
            clickedSlot: 0,
            button: 0,
            stateId: 0,
            clickType: ClickType.QuickMove
        );

        MainEventHandler.OnInventoryClickTest(args);

        Assert.Null(container.GetItem(0));
        Assert.Contains(Enumerable.Range(0, player.Inventory.Size).Select(i =>
        player.Inventory.GetItem((short)i)), i => i == item);
    }

    [Fact]
    public void Clone_ShouldGiveMaxStackInCreative()
    {
        var player = new FakePlayer { GameMode = GameMode.Creative };
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

        MainEventHandler.OnInventoryClickTest(args);

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

        MainEventHandler.OnInventoryClickTest(args);

        var remaining = container.GetItem(0);
        Assert.True(remaining == null || remaining.Count < 5);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 1)]
    [InlineData(90, 0, -1, 0, 0)]
    [InlineData(180, 0, 0, 0, -1)]
    [InlineData(270, 0, 1, 0, 0)]
    [InlineData(0, 90, 0, -1, 0)]
    public void LookDirection_UsesDegrees(float yaw, float pitch, double x, double y, double z)
    {
        var entity = new Obsidian.Entities.Entity { Level = null!, Yaw = yaw, Pitch = pitch };

        var direction = entity.GetLookDirection();

        Assert.InRange(System.Math.Abs(direction.X - x), 0, 0.00001);
        Assert.InRange(System.Math.Abs(direction.Y - y), 0, 0.00001);
        Assert.InRange(System.Math.Abs(direction.Z - z), 0, 0.00001);
    }

    [Theory]
    [InlineData(0, 1, 4)]
    [InlineData(1, 5, 0)]
    public void Throw_PreservesDroppedCountAndComponents(int button, int droppedCount, int remainingCount)
    {
        var player = new FakePlayer();
        var container = new Container(9);
        var source = new ItemStack(ItemsRegistry.Stone, 5,
            Obsidian.API.Inventory.DataComponents.ComponentBuilder.CustomName with { Value = "Named stone" });
        container.SetItem(0, source);

        var dropped = MainEventHandler.ThrowItem(player, container, 0, (sbyte)button);

        Assert.NotNull(dropped);
        Assert.NotSame(source, dropped);
        Assert.Equal(droppedCount, dropped.Count);
        Assert.Equal(source, dropped);
        Assert.Equal(remainingCount, container.GetItem(0)?.Count ?? 0);
        dropped.Count = 0;
        Assert.Equal(remainingCount, container.GetItem(0)?.Count ?? 0);
    }

    [Theory]
    [InlineData(0, 5, 0)]
    [InlineData(1, 1, 4)]
    public void Throw_OutsideClickUsesVanillaButtonCounts(int button, int droppedCount, int remainingCount)
    {
        var player = new FakePlayer { CarriedItem = new ItemStack(ItemsRegistry.Stone, 5) };

        var dropped = MainEventHandler.ThrowItem(player, player.Inventory, -999, (sbyte)button, true);

        Assert.NotNull(dropped);
        Assert.Equal(droppedCount, dropped.Count);
        Assert.Equal(remainingCount, player.CarriedItem?.Count ?? 0);
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

        MainEventHandler.OnInventoryClickTest(args);

        Assert.True(player.CarriedItem.Count > 10);
        Assert.True(container.GetItem(0) == null || container.GetItem(0).Count < 20);
    }
}
