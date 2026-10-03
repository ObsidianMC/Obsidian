using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.API.Loot.Functions;

/// <summary>
/// Sets the stack's custom name or item name.
/// </summary>
/// <remarks>
/// Vanilla can resolve entity-dependent text (selectors, scores) in the name; Obsidian sets the name as is.
/// </remarks>
[LootType("minecraft:set_name")]
public sealed class SetNameFunction : LootFunction
{
    public ChatMessage? Name { get; init; }

    public NameTarget Target { get; init; } = NameTarget.CustomName;

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        if (this.Name is null)
            return stack;

        var component = this.Target == NameTarget.ItemName ? ComponentBuilder.ItemName : ComponentBuilder.CustomName;
        LootItems.Set(stack, component with { Value = this.Name with { } });
        return stack;
    }
}

/// <summary>
/// The name component <see cref="SetNameFunction"/> writes.
/// </summary>
public enum NameTarget
{
    /// <summary>
    /// <c>minecraft:custom_name</c>, shown in italics like a renamed item.
    /// </summary>
    CustomName,

    /// <summary>
    /// <c>minecraft:item_name</c>, the item's default name.
    /// </summary>
    ItemName
}
