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

    public static readonly HexColor Black = new(0, 0, 0);
    public static readonly HexColor DarkBlue = new(0, 0, 42);
    public static readonly HexColor DarkGreen = new(0, 42, 0);
    public static readonly HexColor DarkCyan = new(0, 42, 42);
    public static readonly HexColor DarkRed = new(42, 0, 0);
    public static readonly HexColor Purple = new(42, 0, 42);
    public static readonly HexColor Gold = new(42, 42, 0);
    public static readonly HexColor Gray = new(42, 42, 42);
    public static readonly HexColor DarkGray = new(85, 85, 85);
    public static readonly HexColor Blue = new(85, 85, 255);
    public static readonly HexColor Green = new(0, 128, 0);
    public static readonly HexColor Red = new(255, 0, 0);
    public static readonly HexColor BrightGreen = new(85, 255, 85);
    public static readonly HexColor Cyan = new(85, 255, 255);
    public static readonly HexColor Pink = new(255, 85, 255);
    public static readonly HexColor Yellow = new(255, 255, 85);
    public static readonly HexColor White = new(255, 255, 255);
}
