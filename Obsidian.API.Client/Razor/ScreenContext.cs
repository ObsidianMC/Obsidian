using Obsidian.API.Client.Screens;

namespace Obsidian.API.Client.Razor;

/// <summary>
/// A Razor screen as its components see it, the cascading value the client gives them: take it with
/// <c>[CascadingParameter] public ScreenContext Screen { get; set; }</c>. It's the screen's panel, like a
/// <see cref="ModScreen"/>'s, the menu it shows, and its translations.
/// </summary>
/// <remarks>
/// Components that take it render again each time the client redraws the screen, every client tick, so they show the
/// menu's data as last sent. Set the panel's properties as the component initializes; the client lays the screen out
/// again after its first render.
/// </remarks>
public sealed class ScreenContext
{
    private readonly ModScreen screen;

    internal ScreenContext(ModScreen screen) => this.screen = screen;

    /// <summary>
    /// The menu the screen shows, for a menu type's screen (see
    /// <see cref="IClientRegistry.RegisterMenuScreen{TComponent}(string)"/>); null for a mod's own screen.
    /// </summary>
    public IScreenMenu? Menu => (this.screen as ModMenuScreen)?.Menu;

    /// <summary>
    /// The panel's width, in the GUI's pixels, like <see cref="ModScreen.Width"/>; 176 unless set.
    /// </summary>
    public int Width
    {
        get => this.screen.Width;
        set => this.screen.Width = value;
    }

    /// <summary>
    /// The panel's height, in the GUI's pixels, like <see cref="ModScreen.Height"/>; 166 unless set.
    /// </summary>
    public int Height
    {
        get => this.screen.Height;
        set => this.screen.Height = value;
    }

    /// <summary>
    /// The panel's background, drawn at its size under the components; null for none.
    /// </summary>
    public GuiImage? Background
    {
        get => this.screen.Background;
        set => this.screen.Background = value;
    }

    /// <summary>
    /// For a menu's screen, where the first slot of the player's inventory goes, like
    /// <see cref="ModMenuScreen.Inventory"/>; null to place them with <see cref="SlotGrid"/>s, or not show them.
    /// </summary>
    public GuiPosition? Inventory
    {
        get => (this.screen as ModMenuScreen)?.Inventory;
        set => this.Set(menu => menu.Inventory = value);
    }

    /// <summary>
    /// For a menu's screen, where the menu's title goes, like <see cref="ModMenuScreen.Title"/>.
    /// </summary>
    public GuiPosition Title
    {
        get => (this.screen as ModMenuScreen)?.Title ?? default;
        set => this.Set(menu => menu.Title = value);
    }

    /// <summary>
    /// For a menu's screen, where the "Inventory" label goes, like <see cref="ModMenuScreen.InventoryLabel"/>.
    /// </summary>
    public GuiPosition? InventoryLabel
    {
        get => (this.screen as ModMenuScreen)?.InventoryLabel;
        set => this.Set(menu => menu.InventoryLabel = value);
    }

    /// <summary>
    /// Whether the screen is open.
    /// </summary>
    public bool IsOpen => this.screen.IsOpen;

    /// <summary>
    /// Closes the screen if it's open; a menu's screen closes its menu too.
    /// </summary>
    public void Close() => this.screen.Close();

    /// <summary>
    /// Translates a translation key with the client's language, from vanilla's and the mods' <c>lang</c> files; the
    /// key itself if it has no translation.
    /// </summary>
    public string Translate(string key, params object?[] arguments) =>
        this.screen.Host?.Translate(key, arguments) ?? key;

    /// <summary>
    /// How wide <paramref name="text"/> is drawn, in the GUI's pixels.
    /// </summary>
    public int TextWidth(string text) => this.screen.Host?.TextWidth(text) ?? 0;

    private void Set(Action<ModMenuScreen> set)
    {
        if (this.screen is ModMenuScreen menu)
            set(menu);
    }
}

/// <summary>
/// A screen built with a Razor component, which the client hosts like a screen in code: it draws the component rather
/// than widgets, and the screen's widgets are the slot grids the component renders (see <see cref="SlotGrid"/>).
/// </summary>
internal interface IComponentScreen
{
    /// <summary>
    /// The mod's component, a <see cref="ModComponent"/>.
    /// </summary>
    public Type Component { get; }

    /// <summary>
    /// The component's parameters, copied when the screen opened.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; }

    public ScreenContext Context { get; }
}

/// <summary>
/// A mod's own screen built with a Razor component (see <see cref="IClientScreens.Open{TComponent}"/>).
/// </summary>
internal sealed class ComponentScreen : ModScreen, IComponentScreen
{
    public ComponentScreen(Type component, IReadOnlyDictionary<string, object?> parameters)
    {
        this.Component = component;
        this.Parameters = parameters;
        this.Context = new ScreenContext(this);
    }

    public Type Component { get; }

    public IReadOnlyDictionary<string, object?> Parameters { get; }

    public ScreenContext Context { get; }
}

/// <summary>
/// A menu type's screen built with a Razor component (see
/// <see cref="IClientRegistry.RegisterMenuScreen{TComponent}(string)"/>).
/// </summary>
internal sealed class ComponentMenuScreen : ModMenuScreen, IComponentScreen
{
    public ComponentMenuScreen(IScreenMenu menu, Type component)
        : base(menu)
    {
        this.Component = component;
        this.Context = new ScreenContext(this);
    }

    public Type Component { get; }

    public IReadOnlyDictionary<string, object?> Parameters { get; } = new Dictionary<string, object?>();

    public ScreenContext Context { get; }
}
