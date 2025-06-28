using System.Diagnostics;
using System.Drawing;

namespace Obsidian.API;

[DebuggerDisplay("{ToString(),nq}")]
public readonly struct HexColor
{
    private readonly string hexColor;

    public HexColor(byte r, byte g, byte b)
    {
        hexColor = $"#{r:X2}{g:X2}{b:X2}";
    }

    public HexColor(Color color)
    {
        hexColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    public HexColor(ChatColor color) : this(color.Color)
    {
    }

    public HexColor(string hex)
    {
        hexColor = hex;
    }

    /// <inheritdoc/>
    public readonly override string ToString() => hexColor;

    public static readonly HexColor Black = new("#000000");
    public static readonly HexColor DarkBlue = new("#0000AA");
    public static readonly HexColor DarkGreen = new("#00AA00");
    public static readonly HexColor DarkAqua = new("#00AAAA");
    public static readonly HexColor DarkRed = new("#AA0000");
    public static readonly HexColor DarkPurple = new("#AA00AA");
    public static readonly HexColor Gold = new("#FFAA00");
    public static readonly HexColor Gray = new("#AAAAAA");
    public static readonly HexColor DarkGray = new("#555555");
    public static readonly HexColor Blue = new("#5555FF");
    public static readonly HexColor Green = new("#55FF55");
    public static readonly HexColor Aqua = new("#55FFFF");
    public static readonly HexColor Red = new("#FF5555");
    public static readonly HexColor LightPurple = new("#FF55FF");
    public static readonly HexColor Yellow = new("#FFFF55");
    public static readonly HexColor White = new("#FFFFFF");
}
