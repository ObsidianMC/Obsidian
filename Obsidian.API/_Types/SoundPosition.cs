namespace Obsidian.API;

public struct SoundPosition
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    public SoundPosition(int x, int y, int z)
    {
        X = x * 8;
        Y = y * 8;
        Z = z * 8;
    }

    public SoundPosition(double x, double y, double z)
    {
        X = (int)(x * 8);
        Y = (int)(y * 8);
        Z = (int)(z * 8);
    }

    public bool Match(int x, int y, int z) => X == x && Y == y && Z == z;

    public override string ToString() => $"{X}:{Y}:{Z}";
}
