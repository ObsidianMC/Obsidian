using System;
using System.Drawing;
using System.Linq;

namespace Obsidian.API
{
    public struct ChatColor
    {

        #region Properties
        public Color Color { get; }
        public ConsoleColor? ConsoleColor { get; }
        public char Code { get; }
        public string Name { get; }
        #endregion

        public ChatColor(char code, string name, Color? color = null, ConsoleColor? consoleColor = null)
        {
            this.Code = code;
            this.Name = name;
            if (color.HasValue)
            {
                this.Color = color.Value;
            }
            else
            {
                this.Color = Color.Transparent;
            }
            if (consoleColor.HasValue)
            {
                this.ConsoleColor = consoleColor.Value;
            }
            else
            {
                this.ConsoleColor = null;
            }
        }

        #region Methods for properties
        public char ToCode() => Code;
        public Color ToColor() => Color;
        public string ToName() => Name;
        public System.ConsoleColor? ToConsoleColor() => ConsoleColor;
        #endregion

        #region FromCode
        public static ChatColor FromCode(string code)
        {
            var code_ = code.Replace("&", "").Replace("§", "").ToString().ToLower().ToCharArray().First();
            return FromCode(code_);
        }

        public static ChatColor FromCode(char code)
        {
            return code switch
            {
                '0' => ChatColor.Black,
                '1' => ChatColor.DarkBlue,
                '2' => ChatColor.DarkGreen,
                '3' => ChatColor.DarkCyan,
                '4' => ChatColor.DarkRed,
                '5' => ChatColor.Purple,
                '6' => ChatColor.Gold,
                '7' => ChatColor.Gray,
                '8' => ChatColor.DarkGray,
                '9' => ChatColor.Blue,
                'a' => ChatColor.BrightGreen,
                'b' => ChatColor.Cyan,
                'c' => ChatColor.Red,
                'd' => ChatColor.Pink,
                'e' => ChatColor.Yellow,
                'f' => ChatColor.White,
                'k' => ChatColor.Obfuscated,
                'l' => ChatColor.Bold,
                'm' => ChatColor.Strikethrough,
                'n' => ChatColor.Underline,
                'o' => ChatColor.Italic,
                'r' => ChatColor.Reset,
                _ => ChatColor.Reset // Maybe add an exception?
            };
        }
        #endregion

        public override string ToString() => $"§{Code}";

        #region Colors
        // 0-9
        public static readonly ChatColor Black = new ChatColor('0', "black", Color.FromArgb(0, 0, 0), System.ConsoleColor.Black);
        public static readonly ChatColor DarkBlue = new ChatColor('1', "dark_blue", Color.FromArgb(0, 0, 42), System.ConsoleColor.DarkBlue);
        public static readonly ChatColor DarkGreen = new ChatColor('2', "dark_green", Color.FromArgb(0, 42, 0), System.ConsoleColor.DarkGreen);
        public static readonly ChatColor DarkCyan = new ChatColor('3', "dark_aqua", Color.FromArgb(0, 42, 42), System.ConsoleColor.DarkCyan);
        public static readonly ChatColor DarkRed = new ChatColor('4', "dark_red", Color.FromArgb(42, 0, 0), System.ConsoleColor.DarkRed);
        public static readonly ChatColor Purple = new ChatColor('5', "dark_purple", Color.FromArgb(42, 0, 42), System.ConsoleColor.DarkMagenta);
        public static readonly ChatColor Gold = new ChatColor('6', "gold", Color.FromArgb(42, 42, 0), System.ConsoleColor.DarkYellow);
        public static readonly ChatColor Gray = new ChatColor('7', "gray", Color.FromArgb(42, 42, 42), System.ConsoleColor.Gray);
        public static readonly ChatColor DarkGray = new ChatColor('8', "dark_gray", Color.FromArgb(85, 85, 85), System.ConsoleColor.DarkGray);
        public static readonly ChatColor Blue = new ChatColor('9', "blue", Color.FromArgb(85, 85, 255), System.ConsoleColor.Blue);

        // A-F
        public static readonly ChatColor BrightGreen = new ChatColor('a', "green", Color.FromArgb(85, 255, 85), System.ConsoleColor.Green);
        public static readonly ChatColor Cyan = new ChatColor('b', "aqua", Color.FromArgb(85, 255, 255), System.ConsoleColor.Cyan);
        public static readonly ChatColor Red = new ChatColor('c', "red", Color.FromArgb(255, 85, 85), System.ConsoleColor.Red);
        public static readonly ChatColor Pink = new ChatColor('d', "light_purple", Color.FromArgb(255, 85, 255), System.ConsoleColor.Magenta);
        public static readonly ChatColor Yellow = new ChatColor('e', "yellow", Color.FromArgb(255, 255, 85), System.ConsoleColor.Yellow);
        public static readonly ChatColor White = new ChatColor('f', "white", Color.FromArgb(255, 255, 255), System.ConsoleColor.White);
        #endregion

        #region Effects
        public static readonly ChatColor Obfuscated = new ChatColor('k', "bold");
        public static readonly ChatColor Bold = new ChatColor('l', "bold");
        public static readonly ChatColor Strikethrough = new ChatColor('m', "strikethrough");
        public static readonly ChatColor Underline = new ChatColor('n', "underline");
        public static readonly ChatColor Italic = new ChatColor('o', "italic");
        public static readonly ChatColor Reset = new ChatColor('r', "reset");
        #endregion

    }
}