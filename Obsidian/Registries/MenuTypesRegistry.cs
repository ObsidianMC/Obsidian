using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// The menu types plugins registered, after vanilla's: their ids follow vanilla's, in registration order. They have no
/// behaviour of their own (their containers are the plugin's), so they stay as registered until the server stops.
/// </summary>
internal static class MenuTypesRegistry
{
    /// <summary>
    /// The most rows a plugin's menu has: vanilla's largest generic menu, a double chest, has six.
    /// </summary>
    public const int MaxRows = 6;

    /// <summary>
    /// The most columns a plugin's menu has, as many as a row of the player's inventory.
    /// </summary>
    public const int MaxColumns = 9;

    private static readonly Lock registering = new();

    // Registering replaces the table whole, so lookups never lock.
    private static MenuDefinition[] menus = [];
    private static FrozenDictionary<string, MenuDefinition> byName = FrozenDictionary<string, MenuDefinition>.Empty;

    /// <summary>
    /// Vanilla's menu types' identifiers, indexed by id (the order of <c>WindowType</c>).
    /// </summary>
    public static IReadOnlyList<string> VanillaIdentifiers => VanillaRegistryIds.Get("minecraft:menu");

    /// <summary>
    /// The menu types plugins registered, in id order.
    /// </summary>
    public static IReadOnlyList<MenuDefinition> PluginMenus => menus;

    /// <summary>
    /// Finds a plugin's menu type by its identifier, ignoring case.
    /// </summary>
    public static bool TryGet(string identifier, [NotNullWhen(true)] out MenuDefinition? menu) =>
        byName.TryGetValue(identifier, out menu);

    /// <summary>
    /// Finds a plugin's menu type by its id.
    /// </summary>
    public static bool TryGet(int id, [NotNullWhen(true)] out MenuDefinition? menu)
    {
        var current = menus;
        var index = id - VanillaIdentifiers.Count;

        menu = index >= 0 && index < current.Length ? current[index] : null;
        return menu is not null;
    }

    /// <summary>
    /// Registers a menu type, or returns the one registered before under the same identifier, which keeps its id.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The identifier is in vanilla's namespace, or the menu was registered before with another shape.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The rows or columns are out of range.</exception>
    public static MenuDefinition Register(string identifier, int rows, int columns)
    {
        if (identifier.StartsWith("minecraft:", StringComparison.Ordinal))
            throw new ArgumentException($"{identifier} is in vanilla's namespace.", nameof(identifier));

        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, MaxRows);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, MaxColumns);

        lock (registering)
        {
            // Clients lay out and number its slots by its shape, so it stays until the server restarts.
            if (byName.TryGetValue(identifier, out var existing))
            {
                return existing.Rows == rows && existing.Columns == columns
                    ? existing
                    : throw new ArgumentException($"{identifier}'s shape can't change until the server restarts.");
            }

            var menu = new MenuDefinition(VanillaIdentifiers.Count + menus.Length, identifier, rows, columns);

            menus = [.. menus, menu];
            byName = menus.ToFrozenDictionary(entry => entry.Identifier, StringComparer.OrdinalIgnoreCase);

            return menu;
        }
    }
}
