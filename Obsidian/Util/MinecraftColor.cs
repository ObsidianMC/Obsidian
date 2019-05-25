using System.Drawing;

public struct MinecraftColor
{
    public Color Color { get; }

    public char Code { get; }

    public string Name { get; }

    public MinecraftColor(char code, string name, Color? color = null)
    {
        this.Code = code;
        this.Name = name;
        if (color.HasValue && color.Value != Color.Transparent)
        {
            this.Color = color.Value;
        }
    }

    public override string ToString() => "§" + this.Code;
    

    //0-9
    public static readonly MinecraftColor Black       = new MinecraftColor('0', "black",          Color.FromArgb(0,   0,   0));
    public static readonly MinecraftColor DarkBlue    = new MinecraftColor('1', "dark_blue",      Color.FromArgb(0,   0,   42));
    public static readonly MinecraftColor DarkGreen   = new MinecraftColor('2', "dark_green",     Color.FromArgb(0,   42,  0));
    public static readonly MinecraftColor DarkCyan    = new MinecraftColor('3', "dark_aqua",      Color.FromArgb(0,   42,  42));
    public static readonly MinecraftColor DarkRed     = new MinecraftColor('4', "dark_red",       Color.FromArgb(42,  0,   0));
    public static readonly MinecraftColor Purple      = new MinecraftColor('5', "dark_purple",    Color.FromArgb(42,  0,   42));
    public static readonly MinecraftColor Gold        = new MinecraftColor('6', "gold",           Color.FromArgb(42,  42,  0));
    public static readonly MinecraftColor Gray        = new MinecraftColor('7', "gray",           Color.FromArgb(42,  42,  42));
    public static readonly MinecraftColor DarkGray    = new MinecraftColor('8', "dark_gray",      Color.FromArgb(85,  85,  85));
    public static readonly MinecraftColor Blue        = new MinecraftColor('9', "blue",           Color.FromArgb(85,  85,  255));
    //A-F
    public static readonly MinecraftColor BrightGreen = new MinecraftColor('a', "green",          Color.FromArgb(85,  255, 85));
    public static readonly MinecraftColor Cyan        = new MinecraftColor('b', "aqua",           Color.FromArgb(85,  255, 255));
    public static readonly MinecraftColor Red         = new MinecraftColor('c', "red",            Color.FromArgb(255, 85,  85));
    public static readonly MinecraftColor Pink        = new MinecraftColor('d', "light_purple",   Color.FromArgb(255, 85,  255));
    public static readonly MinecraftColor Yellow      = new MinecraftColor('e', "yellow",         Color.FromArgb(255, 255, 85));
    public static readonly MinecraftColor White       = new MinecraftColor('f', "white",          Color.FromArgb(255, 255, 255));
    //Effects
    public static readonly MinecraftColor Bold        = new MinecraftColor('l', "bold");
    public static readonly MinecraftColor Reset       = new MinecraftColor('r', "reset");
    //public static readonly MinecraftColor Red         = new MinecraftColor('c', "red");
    //public static readonly MinecraftColor Pink        = new MinecraftColor('d', "light_purple");
    //public static readonly MinecraftColor Yellow      = new MinecraftColor('e', "yellow");
    //public static readonly MinecraftColor White       = new MinecraftColor('f', "white");

}