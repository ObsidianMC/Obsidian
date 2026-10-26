using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Xunit;

namespace Obsidian.Tests;

public class FishingTests
{
    [Fact]
    public void FishingLootTableRollsAnOpenWaterCatch()
    {
        var items = LootTables.Gameplay.Fishing.GetRandomItems(new LootContext
        {
            Random = new LegacyRandomSource(1),
            Luck = 0,
            EntityFishingOpenWater = _ => true,
            OriginBiome = _ => "minecraft:ocean",
        });

        Assert.Single(items);
    }

    [Fact]
    public void LureReducesBiteWaitByFiveSecondsPerLevel()
    {
        var rod = new ItemStack(ItemsRegistry.FishingRod, 1);
        Assert.Equal(0, FishingData.LureTicks(rod));

        EnchantmentHelper.Enchant(rod, EnchantmentsRegistry.Lure, 1);
        Assert.Equal(100, FishingData.LureTicks(rod));

        EnchantmentHelper.Enchant(rod, EnchantmentsRegistry.Lure, 2);
        Assert.Equal(200, FishingData.LureTicks(rod));
    }

    [Fact]
    public void LuckOfTheSeaAddsOneLuckPerLevel()
    {
        var rod = new ItemStack(ItemsRegistry.FishingRod, 1);
        EnchantmentHelper.Enchant(rod, EnchantmentsRegistry.LuckOfTheSea, 3);

        Assert.Equal(3, FishingData.LuckBonus(rod));
    }

    [Fact]
    public void FishTagCountsRawAndCookedFish()
    {
        Assert.True(FishingData.IsFish(new ItemStack(ItemsRegistry.Cod, 1)));
        Assert.True(FishingData.IsFish(new ItemStack(ItemsRegistry.CookedSalmon, 1)));
        Assert.False(FishingData.IsFish(new ItemStack(ItemsRegistry.Stick, 1)));
    }
}
