using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.WorldData;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Saplings through the vanilla classes that own them: <c>SaplingBlock</c> and <c>MangrovePropaguleBlock</c> for the
/// random tick (generated into <c>BlocksRegistry</c>), and the <c>TreeGrower</c> each sapling holds, dumped from
/// vanilla's <c>SaplingBlock.treeGrower</c> (<c>sapling_growers.json</c>).
/// </summary>
public sealed class SaplingOwners
{
    [Fact(DisplayName = "Each sapling's random tick is owned by SaplingBlock, the propagule's by MangrovePropaguleBlock")]
    public void RandomTicksAreOwnedByTheirVanillaClass()
    {
        foreach (var sapling in new[]
        {
            BlockIds.OakSapling, BlockIds.SpruceSapling, BlockIds.BirchSapling, BlockIds.JungleSapling,
            BlockIds.AcaciaSapling, BlockIds.CherrySapling, BlockIds.DarkOakSapling, BlockIds.PaleOakSapling
        })
        {
            Assert.Same(RandomTickBehaviors.SaplingBlock, BlocksRegistry.GetRandomTickBehavior(sapling));
        }

        Assert.Same(RandomTickBehaviors.MangrovePropaguleBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.MangrovePropagule));
        Assert.Null(BlocksRegistry.GetRandomTickBehavior(BlockIds.Dirt));
    }

    [Fact(DisplayName = "Each sapling holds the tree grower vanilla gives it, and only saplings hold one")]
    public void TreeGrowersAreDumpedFromVanilla()
    {
        var oak = BlocksRegistry.GetTreeGrower(BlockIds.OakSapling);
        Assert.NotNull(oak);
        Assert.Equal("minecraft:oak", oak.Tree);
        Assert.Equal("minecraft:fancy_oak", oak.SecondaryTree);
        Assert.Equal("minecraft:oak_bees_005", oak.Flowers);
        Assert.Equal(0.1f, oak.SecondaryChance);

        var spruce = BlocksRegistry.GetTreeGrower(BlockIds.SpruceSapling);
        Assert.Equal("minecraft:mega_spruce", spruce!.MegaTree);
        Assert.Equal("minecraft:mega_pine", spruce.SecondaryMegaTree);
        Assert.Equal(0.5f, spruce.SecondaryChance);

        var mangrove = BlocksRegistry.GetTreeGrower(BlockIds.MangrovePropagule);
        Assert.Equal("minecraft:mangrove", mangrove!.Tree);
        Assert.Equal("minecraft:tall_mangrove", mangrove.SecondaryTree);
        Assert.Equal(0.85f, mangrove.SecondaryChance);

        Assert.Null(BlocksRegistry.GetTreeGrower(BlockIds.Dirt));
    }
}
