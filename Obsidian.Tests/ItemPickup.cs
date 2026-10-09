using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Xunit;
using PlayerEntity = Obsidian.Entities.Player;

namespace Obsidian.Tests;

public sealed class ItemPickup
{
    // A player at (0, 64, 0) with a 0.6 by 1.8 box. On foot, vanilla's pickup area reaches 1.425 out horizontally
    // (0.3 + 1 + the item's 0.125) and from 0.75 below the feet (0.5 + the item's height) to 0.5 above the head. Riding a
    // 1.4 by 1.6 vehicle at (0, 63, 0), it reaches 1.825 out (0.7 + 1 + 0.125), down to the vehicle's feet less the
    // item's height, and only up to the top of the player's head.
    [Theory]
    [InlineData(false, 1.42, 64, 0, true)]
    [InlineData(false, 1.43, 64, 0, false)]
    [InlineData(false, 0, 64, -1.42, true)]
    [InlineData(false, 0, 63.26, 0, true)]
    [InlineData(false, 0, 63.25, 0, false)]
    [InlineData(false, 0, 66.29, 0, true)]
    [InlineData(false, 0, 66.3, 0, false)]
    [InlineData(true, 1.82, 63, 0, true)]
    [InlineData(true, 1.83, 63, 0, false)]
    [InlineData(true, 0, 62.76, 0, true)]
    [InlineData(true, 0, 62.75, 0, false)]
    [InlineData(true, 0, 65.79, 0, true)]
    [InlineData(true, 0, 65.8, 0, false)]
    public void PickupAreaMatchesVanilla(bool riding, double x, double y, double z, bool expected)
    {
        var player = new BoundingBox(new VectorD(-0.3, 64, -0.3), new VectorD(0.3, 65.8, 0.3));
        BoundingBox? vehicle = riding ? new BoundingBox(new VectorD(-0.7, 63, -0.7), new VectorD(0.7, 64.6, 0.7)) : null;

        var area = PlayerEntity.GetPickupArea(player, vehicle);

        Assert.Equal(expected, PlayerEntity.IsInPickupArea(area, new VectorD(x, y, z)));
    }

    [Fact]
    public void PickupTopsUpStacksThenFillsTheFirstEmptyHotbarSlot()
    {
        const int heldSlot = 40;
        var inventory = new Container(46);
        inventory.SetItem(45, new ItemStack(ItemsRegistry.Stone, 63));
        inventory.SetItem(9, new ItemStack(ItemsRegistry.Stone, 60));
        var picked = new ItemStack(ItemsRegistry.Stone, 10);

        var changed = PlayerEntity.AddPickedUpItem(inventory, heldSlot, picked);

        Assert.Equal([45, 9, 36], changed);
        Assert.Equal(0, picked.Count);
        Assert.Equal(64, inventory.GetItem(45)!.Count);
        Assert.Equal(64, inventory.GetItem(9)!.Count);
        Assert.Equal(5, inventory.GetItem(36)!.Count);
        // The empty held slot gets no priority over the first empty hotbar slot.
        Assert.Null(inventory.GetItem(heldSlot));
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(true, 0)]
    public void FullInventoryKeepsTheStackUnlessCreative(bool infiniteMaterials, int expectedLeft)
    {
        var inventory = new Container(46);
        for (var slot = 9; slot <= 45; slot++)
            inventory.SetItem(slot, new ItemStack(ItemsRegistry.Dirt, 64));
        var picked = new ItemStack(ItemsRegistry.Stone, 10);

        var changed = PlayerEntity.AddPickedUpItem(inventory, 36, picked, infiniteMaterials);

        Assert.Empty(changed);
        Assert.Equal(expectedLeft, picked.Count);
    }
}
