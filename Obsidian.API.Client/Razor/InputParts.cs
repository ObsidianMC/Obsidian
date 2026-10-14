using Microsoft.AspNetCore.Components;

namespace Obsidian.API.Client.Razor;

/// <summary>
/// Vanilla's button with its text, like <see cref="Screens.Button"/>: 200 by 20 pixels unless sized.
/// </summary>
public sealed class Button : ScreenPart
{
    [Parameter]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Whether it can be pressed.
    /// </summary>
    [Parameter]
    public bool Active { get; set; } = true;

    /// <summary>
    /// Raised on a click, or Enter or Space while it's focused, if it's <see cref="Active"/>.
    /// </summary>
    [Parameter]
    public EventCallback OnClick { get; set; }
}

/// <summary>
/// Vanilla's edit box, like <see cref="Screens.TextField"/>: 200 by 20 pixels unless sized. Bind its text with
/// <c>@bind-Value</c>.
/// </summary>
public sealed class TextField : ScreenPart
{
    [Parameter]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Raised with the new text as the player types.
    /// </summary>
    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    [Parameter]
    public int MaxLength { get; set; } = 32;

    /// <summary>
    /// Text shown, greyed, while the field is empty.
    /// </summary>
    [Parameter]
    public string? Hint { get; set; }

    /// <summary>
    /// Whether to draw the field's border and background; without, only the text, like the creative inventory's
    /// search.
    /// </summary>
    [Parameter]
    public bool Bordered { get; set; } = true;
}

/// <summary>
/// A vertical scroll bar the player drags, or scrolls with the wheel over it, like <see cref="Screens.ScrollBar"/>:
/// 12 pixels wide unless sized. Bind its row with <c>@bind-Row</c>, to the same field as an
/// <see cref="ItemGrid"/>'s <c>@bind-FirstRow</c> to scroll a list.
/// </summary>
public sealed class ScrollBar : ScreenPart
{
    /// <summary>
    /// The row scrolled to, from 0 to <see cref="MaxRow"/>.
    /// </summary>
    [Parameter]
    public int Row { get; set; }

    /// <summary>
    /// Raised with the new row as the player scrolls.
    /// </summary>
    [Parameter]
    public EventCallback<int> RowChanged { get; set; }

    /// <summary>
    /// The last row it scrolls to; 0 disables it.
    /// </summary>
    [Parameter]
    public int MaxRow { get; set; }

    /// <summary>
    /// The handle's GUI sprite, with a <c>_disabled</c> one drawn while <see cref="MaxRow"/> is 0.
    /// </summary>
    [Parameter]
    public string Handle { get; set; } = "container/creative_inventory/scroller";

    [Parameter]
    public int HandleHeight { get; set; } = 15;
}
