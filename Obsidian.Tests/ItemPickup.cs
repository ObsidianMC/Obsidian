using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Xunit;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

public sealed class ItemPickup
{
    // A standing player at (0, 64, 0): vanilla's pickup area reaches 1.425 out horizontally (0.3 + 1 + the item's
    // 0.125) and from 0.75 below the feet (0.5 + the item's height) to 0.5 above the head.
    [Theory]
    [InlineData(1.42, 64, 0, true)]
    [InlineData(1.43, 64, 0, false)]
    [InlineData(0, 64, -1.42, true)]
    [InlineData(0, 63.26, 0, true)]
    [InlineData(0, 63.25, 0, false)]
    [InlineData(0, 66.29, 0, true)]
    [InlineData(0, 66.3, 0, false)]
    public void PickupAreaMatchesVanilla(double x, double y, double z, bool expected) =>
        Assert.Equal(expected, ServerPlayer.IsInPickupArea(new VectorD(0, 64, 0), 0.3, 1.8, new VectorD(x, y, z)));

    [Fact]
    public void PickupTopsUpStacksThenFillsTheFirstEmptyHotbarSlot()
    {
        const int heldSlot = 40;
        var inventory = new Container(46);
        inventory.SetItem(45, new ItemStack(ItemsRegistry.Stone, 63));
        inventory.SetItem(9, new ItemStack(ItemsRegistry.Stone, 60));
        var picked = new ItemStack(ItemsRegistry.Stone, 10);

        var changed = ServerPlayer.AddPickedUpItem(inventory, heldSlot, picked);

        Assert.Equal([45, 9, 36], changed);
        Assert.Equal(0, picked.Count);
        Assert.Equal(64, inventory.GetItem(45)!.Count);
        Assert.Equal(64, inventory.GetItem(9)!.Count);
        Assert.Equal(5, inventory.GetItem(36)!.Count);
        // The empty held slot gets no priority over the first empty hotbar slot.
        Assert.Null(inventory.GetItem(heldSlot));
    }
}
