namespace Obsidian.API;

/// <summary>How a text display's lines are aligned, like vanilla's <c>TextDisplay.Align</c>.</summary>
public enum TextDisplayAlignment : byte
{
    /// <summary>Lines are centered.</summary>
    Center,
    /// <summary>Lines start at the left edge.</summary>
    Left,
    /// <summary>Lines end at the right edge.</summary>
    Right
}
