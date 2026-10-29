using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.WorldData;

/// <summary>
/// A vanilla item class's <c>useOn</c> override, as Obsidian runs it when a player uses an item it owns on a block.
/// </summary>
internal interface IItemUseOnBehavior
{
    /// <returns>Whether the item did something with the block (vanilla's <c>InteractionResult.SUCCESS</c>).</returns>
    ValueTask<bool> UseOnAsync(AbstractLevel level, IPlayer player, ItemStack stack, int slot, Vector position, IBlock block, BlockFace face);
}

/// <summary>
/// Obsidian implementations of vanilla item classes that override <c>Item.useOn</c>. Which item an implementation
/// serves comes from <c>Assets/item_use_on_classes.json</c>, the Mojang class that declares each item's override;
/// <see cref="Counterparts"/> maps those classes to implementations, by full class name. Items whose owner has no
/// counterpart (yet) are handled elsewhere, or not at all.
/// </summary>
internal static partial class ItemUseOnBehaviors
{
    /// <summary>The implemented owners, by vanilla class name.</summary>
    /// <remarks>
    /// Empty for now: the only one was <c>HoeItem</c>, which 26.1 replaced with the data-driven
    /// <c>minecraft:block_transformer</c> item component, so tilling is disabled until Phase 4 of
    /// <c>docs/porting-26.3-plan.md</c> reads that (see <c>docs/known-gaps.md</c>).
    /// </remarks>
    internal static readonly FrozenDictionary<string, IItemUseOnBehavior> Counterparts =
        FrozenDictionary<string, IItemUseOnBehavior>.Empty;

    /// <summary>The Mojang class declaring each item's <c>useOn</c>, by item identifier.</summary>
    internal static readonly FrozenDictionary<string, string> Owners = LoadOwners();

    /// <returns>The behaviour of the vanilla class that owns this item's <c>useOn</c> override, or null.</returns>
    public static IItemUseOnBehavior? For(Item item) =>
        Owners.TryGetValue(item.UnlocalizedName, out var owner) && Counterparts.TryGetValue(owner, out var behavior)
            ? behavior
            : null;

    private static JsonDocument Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Obsidian.Assets.{name}")
            ?? throw new InvalidOperationException($"Missing {name} asset.");
        return JsonDocument.Parse(stream);
    }

    private static FrozenDictionary<string, string> LoadOwners()
    {
        using var document = Load("item_use_on_classes.json");
        return document.RootElement.EnumerateObject().ToFrozenDictionary(item => item.Name, item => item.Value.GetString()!);
    }
}
