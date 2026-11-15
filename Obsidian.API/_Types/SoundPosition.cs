namespace Obsidian.API;

/// <summary>
/// A sound's position in the fixed-point form sound packets use: each coordinate is stored as eighths of a block.
/// </summary>
public record struct SoundPosition
{
    /// <summary>Gets or sets the X coordinate, in eighths of a block.</summary>
    public int X { get; set; }
    /// <summary>Gets or sets the Y coordinate, in eighths of a block.</summary>
    public int Y { get; set; }
    /// <summary>Gets or sets the Z coordinate, in eighths of a block.</summary>
    public int Z { get; set; }

    /// <summary>Creates a sound position from block coordinates.</summary>
    public SoundPosition(int x, int y, int z)
    {
        this.X = x * 8;
        this.Y = y * 8;
        this.Z = z * 8;
    }

    /// <summary>
    /// Creates a sound position from world coordinates, truncating each to the nearest eighth of a block toward zero.
    /// </summary>
    public SoundPosition(double x, double y, double z)
    {
        this.X = (int)(x * 8);
        this.Y = (int)(y * 8);
        this.Z = (int)(z * 8);
    }

    /// <summary>Indicates whether the stored coordinates, in eighths of a block, equal the given ones.</summary>
    public bool Match(int x, int y, int z) => this.X == x && this.Y == y && this.Z == z;

    /// <summary>Returns the stored coordinates as <c>X:Y:Z</c>, in eighths of a block.</summary>
    public override string ToString() => $"{X}:{Y}:{Z}";
}
