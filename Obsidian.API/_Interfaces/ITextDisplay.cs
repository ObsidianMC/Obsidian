namespace Obsidian.API;

/// <summary>
/// A text display, like vanilla's <c>Display.TextDisplay</c>: floating text with no hitbox. Changing any property
/// sends the new state to players who are tracking it.
/// </summary>
public interface ITextDisplay : IEntity
{
    /// <summary>The text shown. It can't be <c>null</c>.</summary>
    public ChatMessage Text { get; set; }

    /// <summary>
    /// Gets or sets which axes the text turns on to face the viewer.
    /// </summary>
    public TextDisplayBillboard Billboard { get; set; }

    /// <summary>The background colour as ARGB.</summary>
    public int BackgroundColor { get; set; }

    /// <summary>The width in pixels at which lines wrap.</summary>
    public int LineWidth { get; set; }

    /// <summary>The text's opacity from 0 to 255.</summary>
    public byte TextOpacity { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text has a shadow.
    /// </summary>
    public bool Shadow { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is visible through blocks.
    /// </summary>
    public bool SeeThrough { get; set; }

    /// <summary>
    /// Gets or sets how lines of the text are aligned.
    /// </summary>
    public TextDisplayAlignment Alignment { get; set; }

    /// <summary>The distance multiplier at which the display stops being shown.</summary>
    public float ViewRange { get; set; }

    /// <summary>The offset of the text from the entity's position, in blocks.</summary>
    public VectorF Translation { get; set; }

    /// <summary>
    /// Gets or sets the text's scale on each axis.
    /// </summary>
    public VectorF Scale { get; set; }

    /// <summary>
    /// Ticks over which a position or rotation change, such as a teleport, is smoothed on the client. Vanilla stores
    /// this as <c>teleport_duration</c>.
    /// </summary>
    public int PositionInterpolationDuration { get; set; }

    /// <summary>
    /// Whether the display is left out of the world's save, so a crash doesn't leave it behind. Like vanilla's saved
    /// displays, it is otherwise kept across restarts.
    /// </summary>
    public bool Transient { get; set; }
}
