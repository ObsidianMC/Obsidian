using Obsidian.API.Inventory;

namespace Obsidian.Plugins;

/// <summary>
/// Checks plugins' creative tab registrations (see <see cref="API.Plugins.IPluginRegistry.RegisterCreativeTab"/>),
/// the same way on a server and on the Obsidian client, which keeps and shows the tabs.
/// </summary>
/// <remarks>
/// A server keeps no tabs, like vanilla's and NeoForge's: the creative inventory is built by the client, the
/// <c>minecraft:creative_mode_tab</c> registry isn't synced, and the creative slot packet accepts any item, so a
/// server-side catalog would have nothing to serve. Checking on both sides makes a mod with a bad registration fail
/// to load on either.
/// </remarks>
internal static class PluginCreativeTabs
{
    /// <summary>
    /// Checks a tab registration.
    /// </summary>
    /// <exception cref="ArgumentException">The identifier isn't in the plugin's namespace or isn't valid.</exception>
    /// <exception cref="InvalidOperationException">The plugin is unloading.</exception>
    public static void CheckTab(PluginContainer container, string identifier, CreativeTabProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(properties.Title, nameof(properties));
        ArgumentNullException.ThrowIfNull(properties.Icon, nameof(properties));
        PluginRegistry.CheckIdentifier(container, identifier);
        CheckItems(properties.Items);
        CheckOpen(container);
    }

    /// <summary>
    /// Checks an addition to a tab, and returns its items.
    /// </summary>
    /// <exception cref="InvalidOperationException">The plugin is unloading.</exception>
    public static Item[] CheckAddition(PluginContainer container, string tab, IEnumerable<Item> items)
    {
        ArgumentException.ThrowIfNullOrEmpty(tab);
        var checkedItems = CheckItems(items);
        CheckOpen(container);

        return checkedItems;
    }

    private static Item[] CheckItems(IEnumerable<Item> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Item[] array = [.. items];
        if (array.Any(item => item is null))
            throw new ArgumentException("The items can't contain null.", nameof(items));

        return array;
    }

    private static void CheckOpen(PluginContainer container)
    {
        if (container.IsClosing)
            throw new InvalidOperationException($"{container.Info.Name} is unloading.");
    }
}
