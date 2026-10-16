using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.WorldData;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Harvest eligibility by tool tier. These follow vanilla's tier rules (the <c>incorrect_for_*_tool</c> tags and
/// <c>correct_for_drops</c> in the item defaults), checked against the expected tiers rather than captured output.
/// </summary>
public class BlockHarvestRules
{
    [Theory]
    [InlineData("minecraft:stone", "minecraft:wooden_pickaxe", true)]
    [InlineData("minecraft:iron_ore", "minecraft:wooden_pickaxe", false)]
    [InlineData("minecraft:stone", "minecraft:stone_pickaxe", true)]
    [InlineData("minecraft:iron_ore", "minecraft:stone_pickaxe", true)]
    [InlineData("minecraft:diamond_ore", "minecraft:stone_pickaxe", false)]
    [InlineData("minecraft:diamond_ore", "minecraft:iron_pickaxe", true)]
    [InlineData("minecraft:obsidian", "minecraft:iron_pickaxe", false)]
    [InlineData("minecraft:obsidian", "minecraft:diamond_pickaxe", true)]
    [InlineData("minecraft:stone", "minecraft:iron_shovel", false)]
    [InlineData("minecraft:dirt", "minecraft:iron_shovel", true)]
    public void HarvestFollowsToolTier(string block, string tool, bool expected) =>
        Assert.Equal(expected, BlockDrops.CanHarvest(BlocksRegistry.Get(block), new ItemStack(ItemsRegistry.Get(tool), 1)));

    [Fact]
    public void BareHandsCanHarvestBlocksThatDontRequireATool()
    {
        Assert.True(BlockDrops.CanHarvest(BlocksRegistry.Get("minecraft:dirt"), ItemStack.Air));
        Assert.False(BlockDrops.CanHarvest(BlocksRegistry.Get("minecraft:stone"), ItemStack.Air));
    }

    [Fact]
    public void RemovingTheDefaultToolComponentMeansNoToolRules()
    {
        var pickaxe = new ItemStack(ItemsRegistry.DiamondPickaxe, 1);
        Assert.True(BlockDrops.CanHarvest(BlocksRegistry.Get("minecraft:stone"), pickaxe));

        pickaxe.RemoveComponents.Add(DataComponentType.Tool);

        Assert.False(BlockDrops.CanHarvest(BlocksRegistry.Get("minecraft:stone"), pickaxe));
    }
}
