using Obsidian.API.Plugins;
using System.Threading;

namespace Obsidian.API;

/// <summary>
/// A menu type a plugin registered with <see cref="IPluginRegistry.RegisterMenu"/>, like a NeoForge mod's
/// <c>MenuType</c>: a grid of <see cref="Rows"/> by <see cref="Columns"/> slots above the player's inventory. Open one
/// with <see cref="IPlayer.OpenInventoryAsync"/> and a <see cref="MenuContainer"/> of the type.
/// </summary>
/// <remarks>
/// The server owns the definition: it keeps its instance, identifier, id and shape until the server stops, even when
/// the plugin reloads or unloads.
/// </remarks>
public sealed class MenuDefinition
{
    internal MenuDefinition(int id, string identifier, int rows, int columns)
    {
        this.Id = id;
        this.Identifier = identifier;
        this.Rows = rows;
        this.Columns = columns;
    }

    /// <summary>
    /// The menu type's identifier, like <c>myplugin:pouch</c>.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// The menu type's id in the menu registry. Vanilla's come first; plugins' follow, in the order they were first
    /// registered.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// How many rows of slots the menu has, from 1 to 6.
    /// </summary>
    public int Rows { get; }

    /// <summary>
    /// How many slots each row has, from 1 to 9.
    /// </summary>
    public int Columns { get; }

    /// <summary>
    /// How many slots the menu has, numbered row by row from 0; the player's inventory follows them, like in
    /// vanilla's menus.
    /// </summary>
    public int SlotCount => this.Rows * this.Columns;

    /// <summary>
    /// The plugin that registered the menu type last, while it's loaded; its menus close when it unloads.
    /// </summary>
    /// <remarks>Change it, and <see cref="IsOpenable"/>, inside <see cref="Gate"/>.</remarks>
    internal IPluginContainer? Owner { get; set; }

    /// <summary>
    /// Whether players can open the type's menus: not from when the plugin that registered it starts unloading until
    /// a loaded plugin registers it again, so that none opens after its menus closed.
    /// </summary>
    /// <remarks>Read it, and open a menu, inside <see cref="Gate"/>.</remarks>
    internal bool IsOpenable { get; set; } = true;

    /// <summary>
    /// Taken to change the type's owner or whether it's openable, and to open one of its menus, so that a menu can't
    /// open as its plugin's menus close.
    /// </summary>
    internal Lock Gate { get; } = new();

    public override string ToString() => this.Identifier;
}

/// <summary>
/// The slots of a plugin's menu (see <see cref="MenuDefinition"/>), opened with
/// <see cref="IPlayer.OpenInventoryAsync"/>. Players move items in and out of it like a chest's;
/// <see cref="Events.ContainerClickEventArgs"/> lets the plugin cancel or handle their clicks.
/// </summary>
/// <remarks>
/// Several players can have the same container open: their clicks take turns, and each sees the others' changes. It
/// closes for its viewers, with <see cref="Events.ContainerClosedEventArgs"/>, when the plugin that registered its
/// menu type unloads; from then until a plugin registers the type again, opening one of its menus does nothing.
/// </remarks>
public sealed class MenuContainer : BaseContainer
{
    /// <summary>
    /// Creates an empty container of <paramref name="menu"/>'s type.
    /// </summary>
    /// <param name="menu">The menu type.</param>
    /// <param name="title">The title the menu's screen shows.</param>
    public MenuContainer(MenuDefinition menu, ChatMessage title) : base(menu.SlotCount)
    {
        this.Menu = menu;
        this.Title = title;
    }

    /// <summary>
    /// The menu type.
    /// </summary>
    public MenuDefinition Menu { get; }
}
