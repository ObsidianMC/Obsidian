namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// The network ids of the recipe slot displays the recipe packets write, from vanilla's <c>minecraft:slot_display</c>
/// registry, which versions renumber (26.3 put two new types before <c>item</c>).
/// </summary>
internal static class SlotDisplayTypes
{
    private static readonly string[] ids = Registries.VanillaRegistryIds.All["minecraft:slot_display"];

    /// <summary>An empty slot.</summary>
    public static readonly int Empty = IdOf("minecraft:empty");

    /// <summary>One item, by its id.</summary>
    public static readonly int Item = IdOf("minecraft:item");

    /// <summary>A stack, as an <c>ItemStackTemplate</c>.</summary>
    public static readonly int ItemStack = IdOf("minecraft:item_stack");

    /// <summary>The items of an item holder set: a tag's name, or a list of items.</summary>
    public static readonly int Tag = IdOf("minecraft:tag");

    /// <summary>Another display's items that have a component, by its type.</summary>
    public static readonly int OnlyWithComponent = IdOf("minecraft:only_with_component");

    /// <summary>Another display's items, holding potions the client cycles through.</summary>
    public static readonly int WithAnyPotion = IdOf("minecraft:with_any_potion");

    /// <summary>A dye display's items dyeing a target display's: what a dye recipe crafts.</summary>
    public static readonly int Dyed = IdOf("minecraft:dyed");

    /// <summary>A list of displays the client cycles through.</summary>
    public static readonly int Composite = IdOf("minecraft:composite");

    private static int IdOf(string type) => Array.IndexOf(ids, type) is var id and >= 0
        ? id
        : throw new InvalidOperationException($"Vanilla has no {type} slot display.");
}
