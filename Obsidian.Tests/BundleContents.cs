using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using System.Collections.Immutable;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Bundles: how stacks merge into them and come out, without losing or duplicating items.
/// </summary>
public sealed class BundleContents
{
    private static ItemStack NewBundle() => new(ItemsRegistry.Bundle);

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
}
