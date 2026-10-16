using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Obsidian.API.Client.Razor;

/// <summary>
/// A building block of a mod's Razor screen, like a <see cref="Panel"/>, a <see cref="Label"/> or a
/// <see cref="SlotGrid"/>: the Razor counterparts of the widgets in <see cref="Screens"/>, which the client draws with
/// its own widgets, like vanilla's.
/// </summary>
/// <remarks>
/// Positions and sizes are in the GUI's pixels, from the top left of the panel the part is in (the screen's, or a
/// <see cref="Panel"/>'s), or offsets in a panel that stacks its parts. The client reads mod objects given to parts,
/// like an <see cref="ItemGrid"/>'s items, and raises their events inside a call into the mod.
/// </remarks>
public abstract class ScreenPart : ComponentBase
{
    /// <summary>
    /// Pixels from the left of the panel it's in.
    /// </summary>
    [Parameter]
    public int X { get; set; }

    /// <summary>
    /// Pixels from the top of the panel it's in.
    /// </summary>
    [Parameter]
    public int Y { get; set; }

    /// <summary>
    /// The part's width, in pixels; null for its natural size (its text's, its content's) or the default its type
    /// says.
    /// </summary>
    [Parameter]
    public int? Width { get; set; }

    /// <summary>
    /// The part's height, in pixels; null for its natural size or its type's default.
    /// </summary>
    [Parameter]
    public int? Height { get; set; }

    /// <summary>
    /// Whether the part is drawn and takes input.
    /// </summary>
    [Parameter]
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Text shown in a tooltip while the mouse is over the part, one line per <c>\n</c>, with vanilla's legacy
    /// <c>§</c> formatting codes; null for none.
    /// </summary>
    [Parameter]
    public string? Tooltip { get; set; }

    // The client's component that draws parts, given each part as its "Part" parameter. Without one, as on a server,
    // parts draw nothing.
    internal static Type? View { get; set; }

    protected sealed override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (View is not Type view)
            return;

        builder.OpenComponent(0, view);
        builder.AddComponentParameter(1, "Part", this);
        builder.CloseComponent();
    }
}

/// <summary>
/// How a <see cref="Panel"/> places its parts.
/// </summary>
public enum PanelFlow
{
    /// <summary>
    /// Each part at its own position.
    /// </summary>
    None,

    /// <summary>
    /// Parts one under another, each moved by its position.
    /// </summary>
    Column,

    /// <summary>
    /// Parts side by side, each moved by its position.
    /// </summary>
    Row
}

/// <summary>
/// Where a <see cref="Panel"/> places stacked parts across the stack, or a <see cref="Label"/> its text within its
/// width.
/// </summary>
public enum PanelAlign
{
    Start,

    Center,

    End
}
