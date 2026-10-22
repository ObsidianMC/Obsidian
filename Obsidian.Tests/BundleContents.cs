using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Entities;
using System.Collections.Immutable;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Bundles: vanilla's weight and capacity rules, what fits, how stacks merge and come out, and the selection.
/// </summary>
public sealed class BundleContents
{
    private static ItemStack NewBundle() => new(ItemsRegistry.Bundle);

    [Fact]
    public void EmptyBundleFitsSixtyFourApples()
    {
        var bundle = NewBundle();

        Assert.Equal(64, Bundles.Capacity(bundle, new ItemStack(ItemsRegistry.Apple, 64)));
        Assert.Equal(64, Bundles.Insert(bundle, new ItemStack(ItemsRegistry.Apple, 100)));
        Assert.Equal(0, Bundles.Capacity(bundle, new ItemStack(ItemsRegistry.Apple)));
    }

    [Fact]
    public void EqualStacksMergeAndGoToTheFront()
    {
        var bundle = NewBundle();
        Bundles.Insert(bundle, new ItemStack(ItemsRegistry.Diamond, 2));
        Bundles.Insert(bundle, new ItemStack(ItemsRegistry.Apple, 3));
        Bundles.Insert(bundle, new ItemStack(ItemsRegistry.Diamond, 1));

        var contents = Bundles.Contents(bundle);
        Assert.Equal(2, contents.Length);
        Assert.Equal(ItemsRegistry.Diamond, contents[0].Holder);
        Assert.Equal(3, contents[0].Count);
        Assert.Equal(ItemsRegistry.Apple, contents[1].Holder);
    }

    [Fact]
    public void ShulkerBoxesDoNotFit()
    {
        var bundle = NewBundle();

        Assert.Equal(0, Bundles.Insert(bundle, new ItemStack(ItemsRegistry.WhiteShulkerBox)));
        Assert.Empty(Bundles.Contents(bundle));
    }

    [Fact]
    public void NestedBundleWeighsSixteenthPlusItsContents()
    {
        var inner = NewBundle();
        Bundles.Insert(inner, new ItemStack(ItemsRegistry.Apple, 16));

        // 1/16 for the bundle itself and 16/64 for the apples: a quarter of the outer bundle's capacity.
        Assert.Equal(new Bundles.Weight(5, 16), Bundles.ItemWeight(inner));
        Assert.Equal(3, Bundles.Capacity(NewBundle(), new ItemStack(inner)));
    }

    [Fact]
    public void BeesInsideWeighOne()
    {
        var nest = new ItemStack(ItemsRegistry.BeeNest, 1, new BeesDataComponent
        {
            Occupants = [(new EntityDataComponent(DataComponentType.EntityData), 0, 0)]
        });

        Assert.Equal(Bundles.Weight.One, Bundles.ItemWeight(nest));
        Assert.Equal(Bundles.Weight.Of(1, new ItemStack(ItemsRegistry.BeeNest).MaxStackSize), Bundles.ItemWeight(new ItemStack(ItemsRegistry.BeeNest)));
    }

    [Fact]
    public void RemoveOneTakesTheSelectedStackOrTheFirst()
    {
        var bundle = NewBundle();
        Bundles.Insert(bundle, new ItemStack(ItemsRegistry.Apple, 1));
        Bundles.Insert(bundle, new ItemStack(ItemsRegistry.Diamond, 1));

        Bundles.ToggleSelection(bundle, 1);
        Assert.Equal(1, bundle.BundleSelection);
        Assert.Equal(ItemsRegistry.Apple, Bundles.RemoveOne(bundle)!.Holder);
        Assert.Equal(-1, bundle.BundleSelection);
        Assert.Equal(ItemsRegistry.Diamond, Bundles.RemoveOne(bundle)!.Holder);
        Assert.Null(Bundles.RemoveOne(bundle));
    }

    [Fact]
    public void ToggleSelectsThenDeselectsAndRejectsOutOfRange()
    {
        var bundle = NewBundle();
        Bundles.Insert(bundle, new ItemStack(ItemsRegistry.Apple, 1));

        Bundles.ToggleSelection(bundle, 5);
        Assert.Equal(-1, bundle.BundleSelection);

        Bundles.ToggleSelection(bundle, 0);
        Assert.Equal(0, bundle.BundleSelection);

        Bundles.ToggleSelection(bundle, 0);
        Assert.Equal(-1, bundle.BundleSelection);
    }

    [Fact]
    public void UseDropsOnFirstTickThenEverySecondTickAfterTenTicks()
    {
        // Vanilla's onUseTick: the first tick (200 remaining), then even remainders below 190.
        Assert.True(Bundles.DropsOnTick(200));
        Assert.False(Bundles.DropsOnTick(199));
        Assert.False(Bundles.DropsOnTick(191));
        Assert.True(Bundles.DropsOnTick(190 - 2));
        Assert.False(Bundles.DropsOnTick(189));
        Assert.True(Bundles.DropsOnTick(2));
    }
}
