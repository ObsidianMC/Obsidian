using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Events;
using Obsidian.Tests.Fakes;
using System.Linq;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

public sealed class Player
{
    [Fact]
    public void Throw_PreservesDroppedCountAndComponents()
    {
        (int Button, int DroppedCount, int RemainingCount)[] cases =
        [
            (0, 1, 4),
            (1, 5, 0)
        ];

        FixtureCases.AssertAll(
            cases,
            row => $"button={row.Button}, droppedCount={row.DroppedCount}, remainingCount={row.RemainingCount}",
            row => AssertThrow_PreservesDroppedCountAndComponents(row.Button, row.DroppedCount, row.RemainingCount));
    }

    private static void AssertThrow_PreservesDroppedCountAndComponents(int button, int droppedCount, int remainingCount)
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

    [Fact]
    public void Throw_OutsideClickUsesVanillaButtonCounts()
    {
        (int Button, int DroppedCount, int RemainingCount)[] cases =
        [
            (0, 5, 0),
            (1, 1, 4)
        ];

        FixtureCases.AssertAll(
            cases,
            row => $"button={row.Button}, droppedCount={row.DroppedCount}, remainingCount={row.RemainingCount}",
            row => AssertThrow_OutsideClickUsesVanillaButtonCounts(row.Button, row.DroppedCount, row.RemainingCount));
    }

    private static void AssertThrow_OutsideClickUsesVanillaButtonCounts(
        int button, int droppedCount, int remainingCount)
    {
        var player = new FakePlayer { CarriedItem = new ItemStack(ItemsRegistry.Stone, 5) };

        var dropped = MainEventHandler.ThrowItem(player, player.Inventory, -999, (sbyte)button, true);

        Assert.NotNull(dropped);
        Assert.Equal(droppedCount, dropped.Count);
        Assert.Equal(remainingCount, player.CarriedItem?.Count ?? 0);
    }
}
