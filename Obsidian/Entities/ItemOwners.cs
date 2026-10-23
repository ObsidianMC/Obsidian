using System.Collections.Frozen;
using System.Reflection;
using Obsidian.API.Inventory;
using System.Text.Json;

namespace Obsidian.Entities;

/// <summary>
/// The Mojang classes that declare item behaviour overrides, by item identifier, from the asset dumps written by
/// <c>VanillaDumper</c>. An item absent from a dump inherits the base <c>Item</c> behaviour.
/// </summary>
internal static class ItemOwners
{
    internal const string BundleClass = "net.minecraft.world.item.BundleItem";
    internal const string BlockItemClass = "net.minecraft.world.item.BlockItem";
    internal const string ShulkerBoxBlockClass = "net.minecraft.world.level.block.ShulkerBoxBlock";

    /// <summary>The class declaring <c>overrideStackedOnOther</c>, by item identifier.</summary>
    internal static readonly FrozenDictionary<string, string> StackedOnOther = Load("item_stacked_on_classes.json");

    /// <summary>The class declaring <c>overrideOtherStackedOnMe</c>, by item identifier.</summary>
    internal static readonly FrozenDictionary<string, string> OtherStackedOnMe = Load("item_other_stacked_on_classes.json");

    /// <summary>The class declaring <c>canFitInsideContainerItems</c>, by item identifier.</summary>
    internal static readonly FrozenDictionary<string, string> ContainerFit = Load("item_container_fit_classes.json");

    /// <summary>The runtime block class of each block item, by item identifier (from <c>BlockItem.getBlock</c>).</summary>
    internal static readonly FrozenDictionary<string, string> BlockClasses = Load("item_block_classes.json");

    internal static bool IsBundle(ItemStack stack) =>
        StackedOnOther.TryGetValue(stack.Holder.UnlocalizedName, out var owner) && owner == BundleClass;

    /// <summary>
    /// Vanilla's <c>canFitInsideContainerItems</c>: a block item of a shulker box can't go in a bundle. Items whose
    /// owner is unknown throw rather than fall back to the base behaviour.
    /// </summary>
    internal static bool FitsInContainerItems(ItemStack stack)
    {
        if (!ContainerFit.TryGetValue(stack.Holder.UnlocalizedName, out var owner))
            return true;

        if (owner != BlockItemClass)
            throw new InvalidOperationException($"Unknown canFitInsideContainerItems owner '{owner}' for {stack.Holder.UnlocalizedName}.");

        if (!BlockClasses.TryGetValue(stack.Holder.UnlocalizedName, out var block))
            throw new InvalidOperationException($"No block class recorded for block item {stack.Holder.UnlocalizedName}.");

        return block != ShulkerBoxBlockClass;
    }

    private static FrozenDictionary<string, string> Load(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Obsidian.Assets.{resource}")
            ?? throw new InvalidOperationException($"Missing {resource} asset.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject().ToFrozenDictionary(item => item.Name, item => item.Value.GetString()!);
    }
}
