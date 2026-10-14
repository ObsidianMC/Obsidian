namespace Obsidian.API.Client.Screens;

/// <summary>
/// Widgets a mod draws on the HUD, like a NeoForge mod's GUI layer: a box of <see cref="Width"/> by
/// <see cref="Height"/> pixels placed at its <see cref="Anchor"/>, moved by <see cref="X"/> and <see cref="Y"/>, with
/// the widgets the mod adds, placed from the box's top left. Register it with
/// <see cref="IClientRegistry.RegisterOverlay"/>.
/// </summary>
/// <remarks>
/// <para>
/// Overlays only show: they draw <see cref="Label"/>s, <see cref="Image"/>s, <see cref="ProgressBar"/>s and
/// <see cref="ItemGrid"/>s (without clicks or tooltips), and leave out widgets that take input. They're drawn with the
/// rest of the HUD while the player is in a world: under screens, like vanilla's HUD, and not while the player hides
/// the HUD (F1).
/// </para>
/// <para>
/// The client calls <see cref="Tick"/> on the game's thread, inside a call into the mod; if it throws, it's logged and
/// the overlay is removed. Widgets are drawn as they are each frame, so the overlay changes them whenever it likes,
/// typically in <see cref="Tick"/>. When the mod unloads, its overlays go.
/// </para>
/// </remarks>
public abstract class ModOverlay : WidgetGroup
{
    /// <summary>
    /// The point of the screen the overlay is placed at, which is the same point of its box: a corner, the middle of
    /// an edge, or the centre.
    /// </summary>
    public OverlayAnchor Anchor { get; set; }

    /// <summary>
    /// How far the box is moved right from its anchor, in the GUI's pixels; negative moves it left.
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// How far the box is moved down from its anchor, in the GUI's pixels; negative moves it up.
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    /// The box's width, in the GUI's pixels, which placing it by its right edge or middle takes into account.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// The box's height, in the GUI's pixels, which placing it by its bottom edge or middle takes into account.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Which of vanilla's HUD the overlay is drawn over. Overlays in the same layer are drawn in the order they were
    /// registered.
    /// </summary>
    public OverlayLayer Layer { get; set; }

    /// <summary>
    /// Runs every client tick, 20 times a second, while the player is in a world, like vanilla's HUD's <c>tick</c>:
    /// update the widgets from what changed, like the client's world (see <see cref="IClientConnection.World"/>).
    /// </summary>
    protected internal virtual void Tick()
    {
    }
}

/// <summary>
/// Where a <see cref="ModOverlay"/> is placed on the screen.
/// </summary>
public enum OverlayAnchor
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Center,
    Right,
    BottomLeft,
    Bottom,
    BottomRight
}

/// <summary>
/// Which of vanilla's HUD a <see cref="ModOverlay"/> is drawn over, like the vanilla layer a NeoForge mod registers
/// its GUI layer below or above.
/// </summary>
public enum OverlayLayer
{
    /// <summary>
    /// Over the hotbar, the player's bars, boss bars, the scoreboard, titles and the debug text, under the chat and
    /// the player list, like NeoForge's layers registered below vanilla's chat.
    /// </summary>
    BelowChat,

    /// <summary>
    /// Over the whole HUD, the chat and the player list included, like NeoForge's layers registered above all.
    /// </summary>
    AboveAll
}
