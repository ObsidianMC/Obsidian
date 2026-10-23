namespace Obsidian.API.Client.Screens;

/// <summary>
/// A piece of a mod's screen, like a label, a button or a grid of slots: the mod adds it to its screen (see
/// <see cref="WidgetGroup.Add"/>) and changes its properties whenever it likes, and the client draws it as they are
/// each frame, with its own widgets, like vanilla's.
/// </summary>
/// <remarks>
/// Positions and sizes are in the GUI's pixels, from the top left of the screen's panel (see
/// <see cref="ModScreen.Width"/>). Widgets are used on the game's thread, where the screen's methods and the widgets'
/// events run.
/// </remarks>
public abstract class Widget
{
    /// <summary>
    /// Pixels from the panel's left edge.
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// Pixels from the panel's top edge.
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    /// The widget's width, in pixels; widgets that size themselves (labels, grids) say so.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// The widget's height, in pixels; widgets that size themselves (labels, grids) say so.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Whether the widget is drawn and takes input.
    /// </summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Text shown in a tooltip while the mouse is over the widget, one line per <c>\n</c>, with vanilla's legacy
    /// <c>§</c> formatting codes; null for none.
    /// </summary>
    public string? Tooltip { get; set; }
}

/// <summary>
/// Widgets drawn together: a screen's (see <see cref="ModScreen"/>).
/// </summary>
public abstract class WidgetGroup
{
    private readonly List<Widget> widgets = [];

    /// <summary>
    /// The widgets, in the order they're drawn: later ones over earlier ones.
    /// </summary>
    public IReadOnlyList<Widget> Widgets => this.widgets;

    /// <summary>
    /// Adds a widget, drawn over those added before it.
    /// </summary>
    /// <returns>The widget, to keep for changing it later.</returns>
    /// <exception cref="ArgumentException">The widget was added already.</exception>
    public TWidget Add<TWidget>(TWidget widget) where TWidget : Widget
    {
        ArgumentNullException.ThrowIfNull(widget);
        if (this.widgets.Contains(widget))
            throw new ArgumentException("The widget was added already.", nameof(widget));

        this.widgets.Add(widget);

        return widget;
    }

    /// <summary>
    /// Removes a widget.
    /// </summary>
    /// <returns>Whether it was there.</returns>
    public bool Remove(Widget widget) => this.widgets.Remove(widget);

    /// <summary>
    /// Removes every widget.
    /// </summary>
    public void ClearWidgets() => this.widgets.Clear();
}

/// <summary>
/// An image from a mod's (or vanilla's) assets, drawn at a widget's size: a GUI sprite, or a region of a whole
/// texture like vanilla's container backgrounds.
/// </summary>
public sealed record GuiImage
{
    private GuiImage(string identifier, bool isSprite, int u, int v)
    {
        this.Identifier = identifier;
        this.IsSprite = isSprite;
        this.U = u;
        this.V = v;
    }

    /// <summary>
    /// The sprite's or texture's identifier.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// Whether it's a GUI sprite rather than a texture region.
    /// </summary>
    public bool IsSprite { get; }

    /// <summary>
    /// The region's left edge in the texture, in its pixels; 0 for a sprite.
    /// </summary>
    public int U { get; }

    /// <summary>
    /// The region's top edge in the texture, in its pixels; 0 for a sprite.
    /// </summary>
    public int V { get; }

    /// <summary>
    /// A GUI sprite, like vanilla's <c>blitSprite</c>: <c>examplemod:machine/arrow</c> for
    /// <c>assets/examplemod/textures/gui/sprites/machine/arrow.png</c>, or <c>widget/button</c> for vanilla's. It's
    /// stretched, tiled or nine-sliced to the widget's size as its <c>.mcmeta</c> says, like vanilla's.
    /// </summary>
    public static GuiImage Sprite(string identifier) => new(identifier, true, 0, 0);

    /// <summary>
    /// A region of a whole texture, like vanilla's <c>blit</c>: <c>examplemod:gui/container/machine</c> for
    /// <c>assets/examplemod/textures/gui/container/machine.png</c>, from (<paramref name="u"/>, <paramref name="v"/>)
    /// and as big as the widget, in the texture's pixels (vanilla's container textures are 256 by 256).
    /// </summary>
    public static GuiImage Texture(string identifier, int u = 0, int v = 0) => new(identifier, false, u, v);
}
