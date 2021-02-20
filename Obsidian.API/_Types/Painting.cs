using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.API
{
    public struct Painting
    {

        public short Id { get; private set; }
        public byte X { get; private set; }
        public byte Y { get; private set; }
        public byte Width { get; private set; }
        public byte Height { get; private set; }


        internal Painting(short id, byte x, byte y, byte width, byte height)
        {
            Id = id;
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }


        public static readonly Painting Kebab = new Painting(0, 0, 0, 16, 16);
        public static readonly Painting Aztec = new Painting(1, 16, 0, 16, 16);
        public static readonly Painting Alban = new Painting(2, 32, 0, 16, 16);
        public static readonly Painting Aztec2 = new Painting(3, 48, 0, 16, 16);
        public static readonly Painting Bomb = new Painting(4, 64, 0, 16, 16);
        public static readonly Painting Plant = new Painting(5, 80, 0, 16, 16);
        public static readonly Painting Wasteland = new Painting(6, 96, 0, 16, 16);
        public static readonly Painting Pool = new Painting(7, 0, 32, 32, 16);
        public static readonly Painting Courbet = new Painting(8, 32, 32, 32, 16);
        public static readonly Painting Sea = new Painting(9, 64, 32, 32, 16);
        public static readonly Painting Sunset = new Painting(10, 96, 32, 32, 16);
        public static readonly Painting Creebet = new Painting(11, 128, 32, 32, 16);
        public static readonly Painting Wanderer = new Painting(12, 0, 64, 16, 32);
        public static readonly Painting Graham = new Painting(13, 16, 64, 16, 32);
        public static readonly Painting Match = new Painting(14, 0, 128, 32, 32);
        public static readonly Painting Bust = new Painting(15, 32, 128, 32, 32);
        public static readonly Painting Stage = new Painting(16, 64, 128, 32, 32);
        public static readonly Painting Void = new Painting(17, 96, 128, 32, 32);
        public static readonly Painting SkullAndRoses = new Painting(18, 128, 128, 32, 32);
        public static readonly Painting Wither = new Painting(19, 160, 128, 32, 32);
        public static readonly Painting Fighters = new Painting(20, 0, 96, 64, 32);
        public static readonly Painting Pointer = new Painting(21, 0, 192, 64, 64);
        public static readonly Painting Pigscene = new Painting(22, 64, 192, 64, 64);
        public static readonly Painting BurningSkull = new Painting(23, 128, 192, 64, 64);
        public static readonly Painting Skeleton = new Painting(24, 192, 64, 64, 48);
        public static readonly Painting DonkeyKong = new Painting(25, 192, 112, 64, 48);


    }
}
