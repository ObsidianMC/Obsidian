namespace Obsidian.API.Client.Screens;

/// <summary>
/// A line of text in vanilla's font. It's as big as its text unless <see cref="Centered"/>.
/// </summary>
public sealed class Label : Widget
{
    /// <summary>
    /// The text, drawn as it is.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// The text's colour, as <c>0xAARRGGBB</c>; vanilla's container labels' dark grey by default.
    /// </summary>
    public uint Color { get; set; } = 0xFF404040;

    /// <summary>
    /// Whether the text has vanilla's drop shadow, which container labels don't.
    /// </summary>
    public bool Shadow { get; set; }

    /// <summary>
    /// Whether the text is centred in the label's <see cref="Widget.Width"/>.
    /// </summary>
    public bool Centered { get; set; }
}

/// <summary>
/// An image, drawn at the widget's size.
/// </summary>
public sealed class Image : Widget
{
    /// <summary>
    /// The image; null draws nothing.
    /// </summary>
    public GuiImage? Source { get; set; }
}

/// <summary>
/// An image shown in part, like a furnace's arrow and flame: the part <see cref="Progress"/> says, from the side
/// <see cref="Direction"/> starts at.
/// </summary>
public sealed class ProgressBar : Widget
{
    /// <summary>
    /// The full bar's image, drawn at the widget's size and cut to its progress.
    /// </summary>
    public GuiImage? Source { get; set; }

    /// <summary>
    /// How full the bar is, from 0 to 1. The part shown is rounded up to whole pixels, like vanilla's furnace arrow.
    /// </summary>
    public float Progress { get; set; }

    /// <summary>
    /// Which way the bar fills.
    /// </summary>
    public ProgressDirection Direction { get; set; }
}

/// <summary>
/// Which way a <see cref="ProgressBar"/> fills.
/// </summary>
public enum ProgressDirection
{
    /// <summary>
    /// From the left edge, like a furnace's arrow.
    /// </summary>
    LeftToRight,

    /// <summary>
    /// From the right edge.
    /// </summary>
    RightToLeft,

    /// <summary>
    /// From the top edge.
    /// </summary>
    TopToBottom,

    /// <summary>
    /// From the bottom edge, like a furnace's flame.
    /// </summary>
    BottomToTop
}
