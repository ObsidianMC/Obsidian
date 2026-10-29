using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Events;
using Obsidian.Tests.Fakes;
using System.Linq;
using Xunit;

namespace Obsidian.Tests;

public sealed class Player
{
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
}
