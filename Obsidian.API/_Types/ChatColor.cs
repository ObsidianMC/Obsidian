using System.Diagnostics;
using System.Drawing;

namespace Obsidian.API;

/// <summary>
/// A legacy chat formatting code: one of the 16 colors or a text style, written after a <c>§</c> sign.
/// </summary>
[DebuggerDisplay("{" + nameof(GetDebuggerDisplay) + "(),nq}")]
public readonly record struct ChatColor
{
    /// <summary>Gets the color's RGB value, or <see cref="Color.Transparent"/> for a style code.</summary>
    public Color Color { get; }
    /// <summary>Gets the closest console color, or null for a style code.</summary>
    public ConsoleColor? ConsoleColor { get; }
    /// <summary>Gets the code's character, such as <c>c</c> for red.</summary>
    public char Code { get; }
    /// <summary>Gets the code's name in chat components, such as <c>dark_red</c>.</summary>
    public string Name { get; }

    private ChatColor(char code, string name, Color? color = null, ConsoleColor? consoleColor = null)
    {
        Code = code;
        Name = name;
        Color = color ?? Color.Transparent;
        ConsoleColor = consoleColor;
    }

    #region FromCode
    /// <summary>
    /// Gets the code whose character is the first one in <paramref name="code"/> that isn't <c>&amp;</c> or <c>§</c>,
    /// ignoring case.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="code"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="code"/> has no such character, or it is not a known
    /// code.</exception>
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

    /// <summary>Gets the code for a character, such as <c>c</c> for red. Only lowercase letters match.</summary>
    /// <exception cref="ArgumentException"><paramref name="code"/> is not a known code.</exception>
    public static ChatColor FromCode(char code)
    {
        if (!TryParse(code, out var color))
            throw new ArgumentException("Character doesn't match any color.", nameof(code));
        return color;
    }

    /// <summary>
    /// Tries to get the code for a character, such as <c>c</c> for red. Only lowercase letters match.
    /// </summary>
    /// <returns>Whether <paramref name="code"/> is a known code.</returns>
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

    /// <summary>Returns the code as a <c>§</c> sign followed by its character.</summary>
    public readonly override string ToString() => $"§{Code}";

    #region Colors
    // 0-9
    /// <summary>The black color.</summary>
    public static readonly ChatColor Black = new('0', "black", Color.FromArgb(0, 0, 0), System.ConsoleColor.Black);
    /// <summary>The dark blue color.</summary>
    public static readonly ChatColor DarkBlue = new('1', "dark_blue", Color.FromArgb(0, 0, 42), System.ConsoleColor.DarkBlue);
    /// <summary>The dark green color.</summary>
    public static readonly ChatColor DarkGreen = new('2', "dark_green", Color.FromArgb(0, 42, 0), System.ConsoleColor.DarkGreen);
    /// <summary>The dark aqua color.</summary>
    public static readonly ChatColor DarkCyan = new('3', "dark_aqua", Color.FromArgb(0, 42, 42), System.ConsoleColor.DarkCyan);
    /// <summary>The dark red color.</summary>
    public static readonly ChatColor DarkRed = new('4', "dark_red", Color.FromArgb(42, 0, 0), System.ConsoleColor.DarkRed);
    /// <summary>The dark purple color.</summary>
    public static readonly ChatColor Purple = new('5', "dark_purple", Color.FromArgb(42, 0, 42), System.ConsoleColor.DarkMagenta);
    /// <summary>The gold color.</summary>
    public static readonly ChatColor Gold = new('6', "gold", Color.FromArgb(42, 42, 0), System.ConsoleColor.DarkYellow);
    /// <summary>The gray color.</summary>
    public static readonly ChatColor Gray = new('7', "gray", Color.FromArgb(42, 42, 42), System.ConsoleColor.Gray);
    /// <summary>The dark gray color.</summary>
    public static readonly ChatColor DarkGray = new('8', "dark_gray", Color.FromArgb(85, 85, 85), System.ConsoleColor.DarkGray);
    /// <summary>The blue color.</summary>
    public static readonly ChatColor Blue = new('9', "blue", Color.FromArgb(85, 85, 255), System.ConsoleColor.Blue);

    // A-F
    /// <summary>The green color.</summary>
    public static readonly ChatColor BrightGreen = new('a', "green", Color.FromArgb(85, 255, 85), System.ConsoleColor.Green);
    /// <summary>The aqua color.</summary>
    public static readonly ChatColor Cyan = new('b', "aqua", Color.FromArgb(85, 255, 255), System.ConsoleColor.Cyan);
    /// <summary>The red color.</summary>
    public static readonly ChatColor Red = new('c', "red", Color.FromArgb(255, 85, 85), System.ConsoleColor.Red);
    /// <summary>The light purple color.</summary>
    public static readonly ChatColor Pink = new('d', "light_purple", Color.FromArgb(255, 85, 255), System.ConsoleColor.Magenta);
    /// <summary>The yellow color.</summary>
    public static readonly ChatColor Yellow = new('e', "yellow", Color.FromArgb(255, 255, 85), System.ConsoleColor.Yellow);
    /// <summary>The white color.</summary>
    public static readonly ChatColor White = new('f', "white", Color.FromArgb(255, 255, 255), System.ConsoleColor.White);
    #endregion

    #region Effects
    /// <summary>Scrambles the text with changing random characters.</summary>
    public static readonly ChatColor Obfuscated = new('k', "obfuscated");
    /// <summary>Makes the text bold.</summary>
    public static readonly ChatColor Bold = new('l', "bold");
    /// <summary>Strikes through the text.</summary>
    public static readonly ChatColor Strikethrough = new('m', "strikethrough");
    /// <summary>Underlines the text.</summary>
    public static readonly ChatColor Underline = new('n', "underline");
    /// <summary>Makes the text italic.</summary>
    public static readonly ChatColor Italic = new('o', "italic");
    /// <summary>Clears the color and styles set before it.</summary>
    public static readonly ChatColor Reset = new('r', "reset");
    #endregion

    private string GetDebuggerDisplay()
    {
        return string.IsNullOrWhiteSpace(Name) ? $"RGB({Color.R}, {Color.G}, {Color.B})" : $"{Name} RGB({Color.R}, {Color.G}, {Color.B})";
    }
}
