using System.Drawing;

namespace Obsidian.API
{
    public struct ChatColor
    {
        public Color Color { get; }
        public char Code { get; }
        public string Name { get; }

        public ChatColor(char code, string name, Color? color = null)
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
        }

        public override string ToString() => $"§{Code}";

        //0-9
        public static readonly ChatColor Black = new ChatColor('0', "black", Color.FromArgb(0, 0, 0));

        public static readonly ChatColor DarkBlue = new ChatColor('1', "dark_blue", Color.FromArgb(0, 0, 42));
        public static readonly ChatColor DarkGreen = new ChatColor('2', "dark_green", Color.FromArgb(0, 42, 0));
        public static readonly ChatColor DarkCyan = new ChatColor('3', "dark_aqua", Color.FromArgb(0, 42, 42));
        public static readonly ChatColor DarkRed = new ChatColor('4', "dark_red", Color.FromArgb(42, 0, 0));
        public static readonly ChatColor Purple = new ChatColor('5', "dark_purple", Color.FromArgb(42, 0, 42));
        public static readonly ChatColor Gold = new ChatColor('6', "gold", Color.FromArgb(42, 42, 0));
        public static readonly ChatColor Gray = new ChatColor('7', "gray", Color.FromArgb(42, 42, 42));
        public static readonly ChatColor DarkGray = new ChatColor('8', "dark_gray", Color.FromArgb(85, 85, 85));
        public static readonly ChatColor Blue = new ChatColor('9', "blue", Color.FromArgb(85, 85, 255));

        //A-F
        public static readonly ChatColor BrightGreen = new ChatColor('a', "green", Color.FromArgb(85, 255, 85));

        public static readonly ChatColor Cyan = new ChatColor('b', "aqua", Color.FromArgb(85, 255, 255));
        public static readonly ChatColor Red = new ChatColor('c', "red", Color.FromArgb(255, 85, 85));
        public static readonly ChatColor Pink = new ChatColor('d', "light_purple", Color.FromArgb(255, 85, 255));
        public static readonly ChatColor Yellow = new ChatColor('e', "yellow", Color.FromArgb(255, 255, 85));
        public static readonly ChatColor White = new ChatColor('f', "white", Color.FromArgb(255, 255, 255));

        //Effects
        public static readonly ChatColor Bold = new ChatColor('l', "bold");

        public static readonly ChatColor Reset = new ChatColor('r', "reset");
        //public static readonly MinecraftColor Red         = new MinecraftColor('c', "red");
        //public static readonly MinecraftColor Pink        = new MinecraftColor('d', "light_purple");
        //public static readonly MinecraftColor Yellow      = new MinecraftColor('e', "yellow");
        //public static readonly MinecraftColor White       = new MinecraftColor('f', "white");
    }
}