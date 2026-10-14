namespace Obsidian.API.Client.Screens;

/// <summary>
/// Vanilla's button: its text on the button sprite, highlighted under the mouse and greyed out while inactive. 200 by
/// 20 pixels unless sized otherwise.
/// </summary>
public sealed class Button : Widget
{
    public Button()
    {
        this.Width = 200;
        this.Height = 20;
    }

    /// <summary>
    /// The button's text.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Whether the player can press the button.
    /// </summary>
    public bool Active { get; set; } = true;

    /// <summary>
    /// Raised when the player presses the button, with a click or with Enter or Space while it has the focus.
    /// </summary>
    public event Action? Pressed;

    // Called by the client.
    internal void Press() => this.Pressed?.Invoke();
}

/// <summary>
/// Vanilla's edit box: a line of text the player types, with a caret, selection and the clipboard. 200 by 20 pixels
/// unless sized otherwise.
/// </summary>
public sealed class TextField : Widget
{
    public TextField()
    {
        this.Width = 200;
        this.Height = 20;
    }

    /// <summary>
    /// The text. Setting it doesn't raise <see cref="Changed"/>.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// How many characters the player can type.
    /// </summary>
    public int MaxLength { get; set; } = 32;

    /// <summary>
    /// Grey text shown while the field is empty and not focused, like vanilla's search hint; null for none.
    /// </summary>
    public string? Hint { get; set; }

    /// <summary>
    /// Whether the field's box is drawn; without it, the text is drawn on the screen's background, like the creative
    /// inventory's search.
    /// </summary>
    public bool Bordered { get; set; } = true;

    /// <summary>
    /// Raised with the new text when the player changes it.
    /// </summary>
    public event Action<string>? Changed;

    // Called by the client as the player types.
    internal void Change(string text)
    {
        this.Text = text;
        this.Changed?.Invoke(text);
    }
}

/// <summary>
/// A vertical scroll bar, like the creative inventory's: a handle the player drags along the bar's height, or moves
/// with the mouse wheel over the bar or its <see cref="WheelArea"/>, to pick a row. The screen shows what the row
/// says, for example as an <see cref="ItemGrid"/>'s <see cref="ItemGrid.FirstRow"/>. 12 pixels wide unless sized
/// otherwise.
/// </summary>
public sealed class ScrollBar : Widget
{
    private int row;
    private int maxRow;

    public ScrollBar() => this.Width = 12;

    /// <summary>
    /// The row scrolled to, from 0 to <see cref="MaxRow"/>. Setting it doesn't raise <see cref="Scrolled"/>.
    /// </summary>
    public int Row
    {
        get => this.row;
        set => this.row = Math.Clamp(value, 0, this.maxRow);
    }

    /// <summary>
    /// The last row the bar scrolls to: how many rows there are beyond those shown at once. At 0 the bar can't move
    /// and its handle is drawn disabled.
    /// </summary>
    public int MaxRow
    {
        get => this.maxRow;
        set
        {
            this.maxRow = Math.Max(0, value);
            this.row = Math.Clamp(this.row, 0, this.maxRow);
        }
    }

    /// <summary>
    /// The handle's GUI sprite, with a <c>_disabled</c> variant drawn while the bar can't move: vanilla's creative
    /// inventory's by default.
    /// </summary>
    public string Handle { get; set; } = "container/creative_inventory/scroller";

    /// <summary>
    /// The handle's height, in pixels; it's as wide as the bar.
    /// </summary>
    public int HandleHeight { get; set; } = 15;

    /// <summary>
    /// Another widget the mouse wheel scrolls the bar over, like the grid it scrolls; null for only the bar.
    /// </summary>
    public Widget? WheelArea { get; set; }

    /// <summary>
    /// Raised with the new row when the player scrolls.
    /// </summary>
    public event Action<int>? Scrolled;

    // Called by the client as the player drags the handle or turns the wheel.
    internal void ScrollTo(int row)
    {
        var before = this.row;
        this.Row = row;
        if (this.row != before)
            this.Scrolled?.Invoke(this.row);
    }
}
