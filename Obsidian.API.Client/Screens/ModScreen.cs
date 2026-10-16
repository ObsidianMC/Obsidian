namespace Obsidian.API.Client.Screens;

/// <summary>
/// A mod's screen, built in code from widgets like a NeoForge mod's <c>Screen</c>: a panel of <see cref="Width"/> by
/// <see cref="Height"/> pixels centred on the darkened game, with an optional <see cref="Background"/> and the widgets
/// the mod adds in <see cref="Init"/>. Open one with <see cref="IClientScreens.Open"/>; a screen for a menu type is a
/// <see cref="ModMenuScreen"/>.
/// </summary>
/// <remarks>
/// <para>
/// The client calls the screen's methods, and raises its widgets' events, on the game's thread, inside a call into the
/// mod: if one throws, it's logged and the screen closes. Widgets are drawn as they are each frame, so the screen
/// changes them whenever it likes, typically in <see cref="Tick"/> and in their events.
/// </para>
/// <para>
/// Escape closes the screen and goes back to what was shown before, as does <see cref="Close"/>. When the mod
/// unloads, its open screens close.
/// </para>
/// </remarks>
public abstract class ModScreen : WidgetGroup
{
    /// <summary>
    /// The panel's width, in the GUI's pixels, like vanilla's container screens' <c>imageWidth</c>.
    /// </summary>
    public int Width { get; set; } = 176;

    /// <summary>
    /// The panel's height, in the GUI's pixels, like vanilla's container screens' <c>imageHeight</c>.
    /// </summary>
    public int Height { get; set; } = 166;

    /// <summary>
    /// The panel's background, drawn at its size under the widgets; null for none.
    /// </summary>
    public GuiImage? Background { get; set; }

    /// <summary>
    /// Whether the screen is open: from just before <see cref="Init"/> until <see cref="Removed"/>.
    /// </summary>
    public bool IsOpen => this.Host is not null;

    // What the client attached the screen to while it's open.
    internal IScreenHost? Host { get; set; }

    /// <summary>
    /// Closes the screen if it's open; a menu's screen closes its menu too.
    /// </summary>
    public void Close() => this.Host?.Close(this);

    /// <summary>
    /// Builds the screen as it opens, like vanilla's <c>init</c>: add its widgets here. The client removes the
    /// widgets before calling it, so widgets added earlier, like in the constructor, don't show.
    /// </summary>
    protected internal virtual void Init()
    {
    }

    /// <summary>
    /// Runs every client tick, 20 times a second, while the screen is open, like vanilla's <c>tick</c>: update the
    /// widgets from what changed, like a menu's data slots.
    /// </summary>
    protected internal virtual void Tick()
    {
    }

    /// <summary>
    /// Runs as the screen closes, like vanilla's <c>removed</c>.
    /// </summary>
    protected internal virtual void Removed()
    {
    }

    /// <summary>
    /// Translates a translation key with the client's language, from vanilla's and the mods' <c>lang</c> files; the
    /// key itself if it has no translation.
    /// </summary>
    protected string Translate(string key, params object?[] arguments) =>
        this.Host?.Translate(key, arguments) ?? key;

    /// <summary>
    /// How wide <paramref name="text"/> is drawn, in the GUI's pixels, to place labels by it.
    /// </summary>
    protected int TextWidth(string text) => this.Host?.TextWidth(text) ?? 0;
}

/// <summary>
/// A mod's screen for one of its menu types, like a NeoForge mod's <c>AbstractContainerScreen</c>: a
/// <see cref="ModScreen"/> with the menu's title and the player's inventory, whose <see cref="SlotGrid"/>s show the
/// menu's slots, and whose <see cref="Menu"/> gives the menu's data and sends its buttons. Register it with
/// <see cref="IClientRegistry.RegisterMenuScreen(string, Func{IScreenMenu, ModMenuScreen})"/>; the client makes one
/// each time the server opens the menu.
/// </summary>
/// <remarks>
/// The player clicks, drags and shift-clicks the slots like any container's, presses a hotbar key to swap with the
/// slot under the mouse, and closes the screen, and the menu, with Escape or the inventory key.
/// </remarks>
/// <param name="menu">The menu the screen shows.</param>
public abstract class ModMenuScreen(IScreenMenu menu) : ModScreen
{
    /// <summary>
    /// The menu the screen shows.
    /// </summary>
    public IScreenMenu Menu { get; } = menu;

    /// <summary>
    /// Where the first slot of the player's inventory goes: its three rows of nine, then the hotbar 58 pixels below
    /// the first row, like vanilla's menus and <see cref="MenuScreenProperties.Inventory"/>; null to place the
    /// player's slots with <see cref="SlotGrid"/>s, or not show them.
    /// </summary>
    public GuiPosition? Inventory { get; set; } = new(8, 84);

    /// <summary>
    /// Where the menu's title goes.
    /// </summary>
    public GuiPosition Title { get; set; } = new(8, 6);

    /// <summary>
    /// Where the "Inventory" label goes; if null, 12 pixels above the player's inventory, or nowhere without it.
    /// </summary>
    public GuiPosition? InventoryLabel { get; set; }
}

/// <summary>
/// What an open screen asks of the client.
/// </summary>
internal interface IScreenHost
{
    public void Close(ModScreen screen);

    public string Translate(string key, object?[] arguments);

    public int TextWidth(string text);
}
