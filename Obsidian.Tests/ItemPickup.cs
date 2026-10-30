using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Xunit;
using PlayerEntity = Obsidian.Entities.Player;

namespace Obsidian.Tests;

public sealed class ItemPickup
{
    [Fact]
    public void PickupTopsUpStacksThenFillsTheFirstEmptyHotbarSlot()
    {
        const int heldSlot = 40;
        var inventory = new Container(46);
        inventory.SetItem(45, new ItemStack(ItemsRegistry.Stone, 63));
        inventory.SetItem(9, new ItemStack(ItemsRegistry.Stone, 60));
        var picked = new ItemStack(ItemsRegistry.Stone, 10);

        var changed = PlayerEntity.AddToInventory(inventory, heldSlot, picked);

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

        var changed = PlayerEntity.AddToInventory(inventory, 36, picked, infiniteMaterials);

        Assert.Empty(changed);
        Assert.Equal(expectedLeft, picked.Count);
    }
}
