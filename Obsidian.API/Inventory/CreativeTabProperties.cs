namespace Obsidian.API.Inventory;

/// <summary>
/// A plugin's creative tab, given when registering it (see
/// <see cref="Plugins.IPluginRegistry.RegisterCreativeTab"/>): its title, its icon, where it goes among the tabs and
/// the items it shows.
/// </summary>
public sealed class CreativeTabProperties
{
    /// <summary>
    /// The tab's title, shown above its items and as its tooltip: a translation key
    /// (<c>new ChatMessage { Translate = "itemGroup.ruby" }</c>, translated with the mod's language files) or plain
    /// text (<see cref="ChatMessage.Simple(string)"/>).
    /// </summary>
    public required ChatMessage Title { get; init; }

    /// <summary>
    /// The item drawn on the tab.
    /// </summary>
    public required Item Icon { get; init; }

    /// <summary>
    /// The tab this one follows, like NeoForge's <c>withTabsBefore</c>: one of vanilla's, like
    /// <c>minecraft:spawn_eggs</c>, or another plugin's. Null, or a tab that isn't registered (yet), puts it after
    /// vanilla's tabs and the tabs registered before it.
    /// </summary>
    public string? After { get; init; }

    /// <summary>
    /// The items the tab shows, in order, before those <see cref="DisplayItems"/> gives.
    /// </summary>
    public IReadOnlyList<Item> Items { get; init; } = [];

    /// <summary>
    /// Gives more items for the tab to show, after <see cref="Items"/>, like NeoForge's
    /// <c>DisplayItemsGenerator</c>: for contents that change, called each time the client builds the tabs (as the
    /// creative inventory opens). It runs on the client only, inside a call into the plugin; if it throws, the error
    /// is logged and the tab shows <see cref="Items"/>.
    /// </summary>
    public Func<IEnumerable<Item>>? DisplayItems { get; init; }
}
