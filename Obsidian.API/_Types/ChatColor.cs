using System;
using System.Diagnostics;
using System.Drawing;

namespace Obsidian.API;

[DebuggerDisplay("{" + nameof(GetDebuggerDisplay) + "(),nq}")]
public readonly struct ChatColor
{
    public Color Color { get; }
    public ConsoleColor? ConsoleColor { get; }
    public char Code { get; }
    public string Name { get; }

    private ChatColor(char code, string name, Color? color = null, ConsoleColor? consoleColor = null)
    {
        Code = code;
        Name = name;
        Color = color ?? Color.Transparent;
        ConsoleColor = consoleColor;
    }

    #region FromCode
    public static ChatColor FromCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        for (int i = 0; i < code.Length; i++)
        {
            char character = code[i];
            if (character != '&' && character != '§')
            {
                return FromCode(char.ToLower(character));
            }
        }

        throw new ArgumentException("String doesn't contain any valid code characters.", nameof(code));
    }

    public static ChatColor FromCode(char code)
    {
        if (!TryParse(code, out var color))
            throw new ArgumentException("Character doesn't match any color.", nameof(code));
        return color;
    }

    public static bool TryParse(char code, out ChatColor color)
    {
        color = code switch
        {
            '0' => Black,
            '1' => DarkBlue,
            '2' => DarkGreen,
            '3' => DarkCyan,
            '4' => DarkRed,
            '5' => Purple,
            '6' => Gold,
            '7' => Gray,
            '8' => DarkGray,
            '9' => Blue,
            'a' => BrightGreen,
            'b' => Cyan,
            'c' => Red,
            'd' => Pink,
            'e' => Yellow,
            'f' => White,
            'k' => Obfuscated,
            'l' => Bold,
            'm' => Strikethrough,
            'n' => Underline,
            'o' => Italic,
            'r' => Reset,
            _ => default
        };

        return color.Code != default;
    }
    #endregion

    public readonly override string ToString() => $"§{Code}";

    #region Colors
    // 0-9
    public static readonly ChatColor Black = new('0', "black", Color.FromArgb(0, 0, 0), System.ConsoleColor.Black);
    public static readonly ChatColor DarkBlue = new('1', "dark_blue", Color.FromArgb(0, 0, 42), System.ConsoleColor.DarkBlue);
    public static readonly ChatColor DarkGreen = new('2', "dark_green", Color.FromArgb(0, 42, 0), System.ConsoleColor.DarkGreen);
    public static readonly ChatColor DarkCyan = new('3', "dark_aqua", Color.FromArgb(0, 42, 42), System.ConsoleColor.DarkCyan);
    public static readonly ChatColor DarkRed = new('4', "dark_red", Color.FromArgb(42, 0, 0), System.ConsoleColor.DarkRed);
    public static readonly ChatColor Purple = new('5', "dark_purple", Color.FromArgb(42, 0, 42), System.ConsoleColor.DarkMagenta);
    public static readonly ChatColor Gold = new('6', "gold", Color.FromArgb(42, 42, 0), System.ConsoleColor.DarkYellow);
    public static readonly ChatColor Gray = new('7', "gray", Color.FromArgb(42, 42, 42), System.ConsoleColor.Gray);
    public static readonly ChatColor DarkGray = new('8', "dark_gray", Color.FromArgb(85, 85, 85), System.ConsoleColor.DarkGray);
    public static readonly ChatColor Blue = new('9', "blue", Color.FromArgb(85, 85, 255), System.ConsoleColor.Blue);

    // A-F
    public static readonly ChatColor BrightGreen = new('a', "green", Color.FromArgb(85, 255, 85), System.ConsoleColor.Green);
    public static readonly ChatColor Cyan = new('b', "aqua", Color.FromArgb(85, 255, 255), System.ConsoleColor.Cyan);
    public static readonly ChatColor Red = new('c', "red", Color.FromArgb(255, 85, 85), System.ConsoleColor.Red);
    public static readonly ChatColor Pink = new('d', "light_purple", Color.FromArgb(255, 85, 255), System.ConsoleColor.Magenta);
    public static readonly ChatColor Yellow = new('e', "yellow", Color.FromArgb(255, 255, 85), System.ConsoleColor.Yellow);
    public static readonly ChatColor White = new('f', "white", Color.FromArgb(255, 255, 255), System.ConsoleColor.White);
    #endregion

    #region Effects
    public static readonly ChatColor Obfuscated = new('k', "obfuscated");
    public static readonly ChatColor Bold = new('l', "bold");
    public static readonly ChatColor Strikethrough = new('m', "strikethrough");
    public static readonly ChatColor Underline = new('n', "underline");
    public static readonly ChatColor Italic = new('o', "italic");
    public static readonly ChatColor Reset = new('r', "reset");
    #endregion

    private string GetDebuggerDisplay()
    {
        return string.IsNullOrWhiteSpace(Name) ? $"RGB({Color.R}, {Color.G}, {Color.B})" : $"{Name} RGB({Color.R}, {Color.G}, {Color.B})";
    }
}
