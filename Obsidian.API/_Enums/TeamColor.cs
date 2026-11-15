namespace Obsidian.API;

/// <summary>
/// A team's chat formatting code. Only the 16 colors are sent to clients as the team color; the others are treated as
/// no color.
/// </summary>
public enum TeamColor : int
{
    /// <summary>The black chat color.</summary>
    Black,
    /// <summary>The dark blue chat color.</summary>
    DarkBlue,
    /// <summary>The dark green chat color.</summary>
    DarkGreen,
    /// <summary>The dark aqua chat color.</summary>
    DarkCyan,
    /// <summary>The dark red chat color.</summary>
    DarkRed,
    /// <summary>The dark purple chat color.</summary>
    Purple,
    /// <summary>The gold chat color.</summary>
    Gold,
    /// <summary>The gray chat color.</summary>
    Gray,
    /// <summary>The dark gray chat color.</summary>
    DarkGray,
    /// <summary>The blue chat color.</summary>
    Blue,
    /// <summary>The green chat color.</summary>
    BrightGreen,
    /// <summary>The aqua chat color.</summary>
    Cyan,
    /// <summary>The red chat color.</summary>
    Red,
    /// <summary>The light purple chat color.</summary>
    Pink,
    /// <summary>The yellow chat color.</summary>
    Yellow,
    /// <summary>The white chat color.</summary>
    White,

    /// <summary>Scrambled text formatting; not a color.</summary>
    Obfuscated,
    /// <summary>Bold formatting; not a color.</summary>
    Bold,
    /// <summary>Strikethrough formatting; not a color.</summary>
    Strikethrough,
    /// <summary>Underline formatting; not a color.</summary>
    Underlined,
    /// <summary>Italic formatting; not a color.</summary>
    Italic,
    /// <summary>The formatting reset code; not a color.</summary>
    Reset
}
