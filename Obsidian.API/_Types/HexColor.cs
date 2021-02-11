using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.API
{
    public struct HexColor
    {

        private string hexColor;

        public HexColor(byte r, byte g, byte b)
        {
            hexColor = $"#{r:X2}{g:X2}{b:X2}";
        }

        public HexColor(Color color)
        { 
            hexColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        public HexColor(string hex)
        {
            hexColor = hex;
        }

        public override string ToString() => hexColor;


        public static readonly HexColor Black = new HexColor(Color.FromArgb(0, 0, 0));
        public static readonly HexColor DarkBlue = new HexColor(Color.FromArgb(0, 0, 42));
        public static readonly HexColor DarkGreen = new HexColor(Color.FromArgb(0, 42, 0));
        public static readonly HexColor DarkCyan = new HexColor(Color.FromArgb(0, 42, 42));
        public static readonly HexColor DarkRed = new HexColor(Color.FromArgb(42, 0, 0));
        public static readonly HexColor Purple = new HexColor(Color.FromArgb(42, 0, 42));
        public static readonly HexColor Gold = new HexColor(Color.FromArgb(42, 42, 0));
        public static readonly HexColor Gray = new HexColor(Color.FromArgb(42, 42, 42));
        public static readonly HexColor DarkGray = new HexColor(Color.FromArgb(85, 85, 85));
        public static readonly HexColor Blue = new HexColor(Color.FromArgb(85, 85, 255));
        public static readonly HexColor Green = new HexColor(Color.FromArgb(0, 128, 0));
        public static readonly HexColor Red = new HexColor(Color.FromArgb(255, 0, 0));
        public static readonly HexColor BrightGreen = new HexColor(Color.FromArgb(85, 255, 85));
        public static readonly HexColor Cyan = new HexColor(Color.FromArgb(85, 255, 255));
        public static readonly HexColor Pink = new HexColor(Color.FromArgb(255, 85, 255));
        public static readonly HexColor Yellow = new HexColor(Color.FromArgb(255, 255, 85));
        public static readonly HexColor White = new HexColor(Color.FromArgb(255, 255, 255));

    }
}
