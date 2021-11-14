namespace Obsidian.API;

public readonly struct Painting
{
    public short Id { get; }
    public byte X { get; }
    public byte Y { get; }
    public byte Width { get; }
    public byte Height { get; }

    private Painting(short id, byte x, byte y, byte width, byte height)
    {
        Id = id;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public static readonly Painting Kebab = new(0, 0, 0, 16, 16);
    public static readonly Painting Aztec = new(1, 16, 0, 16, 16);
    public static readonly Painting Alban = new(2, 32, 0, 16, 16);
    public static readonly Painting Aztec2 = new(3, 48, 0, 16, 16);
    public static readonly Painting Bomb = new(4, 64, 0, 16, 16);
    public static readonly Painting Plant = new(5, 80, 0, 16, 16);
    public static readonly Painting Wasteland = new(6, 96, 0, 16, 16);
    public static readonly Painting Pool = new(7, 0, 32, 32, 16);
    public static readonly Painting Courbet = new(8, 32, 32, 32, 16);
    public static readonly Painting Sea = new(9, 64, 32, 32, 16);
    public static readonly Painting Sunset = new(10, 96, 32, 32, 16);
    public static readonly Painting Creebet = new(11, 128, 32, 32, 16);
    public static readonly Painting Wanderer = new(12, 0, 64, 16, 32);
    public static readonly Painting Graham = new(13, 16, 64, 16, 32);
    public static readonly Painting Match = new(14, 0, 128, 32, 32);
    public static readonly Painting Bust = new(15, 32, 128, 32, 32);
    public static readonly Painting Stage = new(16, 64, 128, 32, 32);
    public static readonly Painting Void = new(17, 96, 128, 32, 32);
    public static readonly Painting SkullAndRoses = new(18, 128, 128, 32, 32);
    public static readonly Painting Wither = new(19, 160, 128, 32, 32);
    public static readonly Painting Fighters = new(20, 0, 96, 64, 32);
    public static readonly Painting Pointer = new(21, 0, 192, 64, 64);
    public static readonly Painting Pigscene = new(22, 64, 192, 64, 64);
    public static readonly Painting BurningSkull = new(23, 128, 192, 64, 64);
    public static readonly Painting Skeleton = new(24, 192, 64, 64, 48);
    public static readonly Painting DonkeyKong = new(25, 192, 112, 64, 48);
}
