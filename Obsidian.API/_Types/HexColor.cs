using System.Diagnostics;
using System.Drawing;

namespace Obsidian.API;

/// <summary>A text color written as a <c>#RRGGBB</c> hex string, as chat components use it.</summary>
[DebuggerDisplay("{ToString(),nq}")]
public readonly record struct HexColor
{
    private readonly string hexColor;

    /// <summary>Creates a color from its red, green and blue channels.</summary>
    public HexColor(byte r, byte g, byte b)
    {
        hexColor = $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>Creates a color from a <see cref="Color"/>, ignoring its alpha.</summary>
    public HexColor(Color color)
    {
        hexColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    /// <summary>Creates a color from a chat color's <see cref="ChatColor.Color"/>.</summary>
    public HexColor(ChatColor color) : this(color.Color)
    {
    }

    /// <summary>Creates a color from a hex string, which is used as given without being checked.</summary>
    /// <param name="hex">The color, such as <c>#FF5555</c>.</param>
    public HexColor(string hex)
    {
        hexColor = hex;
    }

    /// <inheritdoc/>
    public readonly override string ToString() => hexColor;

    /// <summary>The black chat color, <c>#000000</c>.</summary>
    public static readonly HexColor Black = new("#000000");
    /// <summary>The dark blue chat color, <c>#0000AA</c>.</summary>
    public static readonly HexColor DarkBlue = new("#0000AA");
    /// <summary>The dark green chat color, <c>#00AA00</c>.</summary>
    public static readonly HexColor DarkGreen = new("#00AA00");
    /// <summary>The dark aqua chat color, <c>#00AAAA</c>.</summary>
    public static readonly HexColor DarkAqua = new("#00AAAA");
    /// <summary>The dark red chat color, <c>#AA0000</c>.</summary>
    public static readonly HexColor DarkRed = new("#AA0000");
    /// <summary>The dark purple chat color, <c>#AA00AA</c>.</summary>
    public static readonly HexColor DarkPurple = new("#AA00AA");
    /// <summary>The gold chat color, <c>#FFAA00</c>.</summary>
    public static readonly HexColor Gold = new("#FFAA00");
    /// <summary>The gray chat color, <c>#AAAAAA</c>.</summary>
    public static readonly HexColor Gray = new("#AAAAAA");
    /// <summary>The dark gray chat color, <c>#555555</c>.</summary>
    public static readonly HexColor DarkGray = new("#555555");
    /// <summary>The blue chat color, <c>#5555FF</c>.</summary>
    public static readonly HexColor Blue = new("#5555FF");
    /// <summary>The green chat color, <c>#55FF55</c>.</summary>
    public static readonly HexColor Green = new("#55FF55");
    /// <summary>The aqua chat color, <c>#55FFFF</c>.</summary>
    public static readonly HexColor Aqua = new("#55FFFF");
    /// <summary>The red chat color, <c>#FF5555</c>.</summary>
    public static readonly HexColor Red = new("#FF5555");
    /// <summary>The light purple chat color, <c>#FF55FF</c>.</summary>
    public static readonly HexColor LightPurple = new("#FF55FF");
    /// <summary>The yellow chat color, <c>#FFFF55</c>.</summary>
    public static readonly HexColor Yellow = new("#FFFF55");
    /// <summary>The white chat color, <c>#FFFFFF</c>.</summary>
    public static readonly HexColor White = new("#FFFFFF");
}
