using Microsoft.AspNetCore.Components;
using Obsidian.API.Client.Screens;

namespace Obsidian.API.Client.Razor;

/// <summary>
/// A box holding parts, its child content: placed by their positions, or stacked in a column or a row.
/// </summary>
public sealed class Panel : ScreenPart
{
    /// <summary>
    /// How the panel places its parts.
    /// </summary>
    [Parameter]
    public PanelFlow Flow { get; set; }

    /// <summary>
    /// The space between stacked parts.
    /// </summary>
    [Parameter]
    public int Gap { get; set; }

    /// <summary>
    /// The space between the panel's edges and its parts.
    /// </summary>
    [Parameter]
    public int Padding { get; set; }

    /// <summary>
    /// Where stacked parts are placed across the stack.
    /// </summary>
    [Parameter]
    public PanelAlign Align { get; set; }

    /// <summary>
    /// Whether to cut off what the parts draw outside the panel, like a scrolling list's.
    /// </summary>
    [Parameter]
    public bool Clip { get; set; }

    /// <summary>
    /// An ARGB color to fill the panel with; null for none.
    /// </summary>
    [Parameter]
    public uint? Color { get; set; }

    /// <summary>
    /// Gets or sets the parts the panel holds, from the markup between its tags.
    /// </summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}

/// <summary>
/// A line of text, like <see cref="Screens.Label"/>: dark grey without a shadow like container labels, unless its
/// <see cref="Color"/> and <see cref="Shadow"/> say otherwise. Its natural size is its text's.
/// </summary>
public sealed class Label : ScreenPart
{
    /// <summary>
    /// Gets or sets the text, drawn as it is; empty by default.
    /// </summary>
    [Parameter]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// The ARGB text color.
    /// </summary>
    [Parameter]
    public uint Color { get; set; } = 0xFF404040;

    /// <summary>
    /// Gets or sets whether the text has vanilla's drop shadow, which container labels don't have.
    /// </summary>
    [Parameter]
    public bool Shadow { get; set; }

    /// <summary>
    /// Where the text goes within the label's <see cref="ScreenPart.Width"/>.
    /// </summary>
    [Parameter]
    public PanelAlign Align { get; set; }
}

/// <summary>
/// A <see cref="GuiImage"/> at the part's size, like <see cref="Screens.Image"/>.
/// </summary>
public sealed class Image : ScreenPart
{
    /// <summary>
    /// Gets or sets the image, drawn at the part's size; null draws nothing.
    /// </summary>
    [Parameter, EditorRequired]
    public GuiImage? Source { get; set; }
}

/// <summary>
/// A <see cref="GuiImage"/> cut to its progress, like <see cref="Screens.ProgressBar"/>.
/// </summary>
public sealed class ProgressBar : ScreenPart
{
    /// <summary>
    /// Gets or sets the full bar's image, drawn at the part's size and cut to its <see cref="Progress"/>.
    /// </summary>
    [Parameter, EditorRequired]
    public GuiImage? Source { get; set; }

    /// <summary>
    /// How much is shown, from 0 to 1, rounded up to whole pixels like a furnace's arrow.
    /// </summary>
    [Parameter]
    public float Progress { get; set; }

    /// <summary>
    /// The side it fills from.
    /// </summary>
    [Parameter]
    public ProgressDirection Direction { get; set; }
}
