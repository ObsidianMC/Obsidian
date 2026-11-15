namespace Obsidian.API;

/// <summary>
/// One of the original 26 painting motifs, with its id and its area in the old shared painting texture.
/// </summary>
public readonly record struct Painting
{
    /// <summary>Gets the motif's id.</summary>
    public short Id { get; }
    /// <summary>Gets the left edge of the motif in the old painting texture, in pixels.</summary>
    public byte X { get; }
    /// <summary>Gets the top edge of the motif in the old painting texture, in pixels.</summary>
    public byte Y { get; }
    /// <summary>Gets the motif's width in pixels; 16 pixels make one block.</summary>
    public byte Width { get; }
    /// <summary>Gets the motif's height in pixels; 16 pixels make one block.</summary>
    public byte Height { get; }

    private Painting(short id, byte x, byte y, byte width, byte height)
    {
        Id = id;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>The <c>kebab</c> painting.</summary>
    public static readonly Painting Kebab = new(0, 0, 0, 16, 16);
    /// <summary>The <c>aztec</c> painting.</summary>
    public static readonly Painting Aztec = new(1, 16, 0, 16, 16);
    /// <summary>The <c>alban</c> painting.</summary>
    public static readonly Painting Alban = new(2, 32, 0, 16, 16);
    /// <summary>The <c>aztec2</c> painting.</summary>
    public static readonly Painting Aztec2 = new(3, 48, 0, 16, 16);
    /// <summary>The <c>bomb</c> painting.</summary>
    public static readonly Painting Bomb = new(4, 64, 0, 16, 16);
    /// <summary>The <c>plant</c> painting.</summary>
    public static readonly Painting Plant = new(5, 80, 0, 16, 16);
    /// <summary>The <c>wasteland</c> painting.</summary>
    public static readonly Painting Wasteland = new(6, 96, 0, 16, 16);
    /// <summary>The <c>pool</c> painting.</summary>
    public static readonly Painting Pool = new(7, 0, 32, 32, 16);
    /// <summary>The <c>courbet</c> painting.</summary>
    public static readonly Painting Courbet = new(8, 32, 32, 32, 16);
    /// <summary>The <c>sea</c> painting.</summary>
    public static readonly Painting Sea = new(9, 64, 32, 32, 16);
    /// <summary>The <c>sunset</c> painting.</summary>
    public static readonly Painting Sunset = new(10, 96, 32, 32, 16);
    /// <summary>The <c>creebet</c> painting.</summary>
    public static readonly Painting Creebet = new(11, 128, 32, 32, 16);
    /// <summary>The <c>wanderer</c> painting.</summary>
    public static readonly Painting Wanderer = new(12, 0, 64, 16, 32);
    /// <summary>The <c>graham</c> painting.</summary>
    public static readonly Painting Graham = new(13, 16, 64, 16, 32);
    /// <summary>The <c>match</c> painting.</summary>
    public static readonly Painting Match = new(14, 0, 128, 32, 32);
    /// <summary>The <c>bust</c> painting.</summary>
    public static readonly Painting Bust = new(15, 32, 128, 32, 32);
    /// <summary>The <c>stage</c> painting.</summary>
    public static readonly Painting Stage = new(16, 64, 128, 32, 32);
    /// <summary>The <c>void</c> painting.</summary>
    public static readonly Painting Void = new(17, 96, 128, 32, 32);
    /// <summary>The <c>skull_and_roses</c> painting.</summary>
    public static readonly Painting SkullAndRoses = new(18, 128, 128, 32, 32);
    /// <summary>The <c>wither</c> painting.</summary>
    public static readonly Painting Wither = new(19, 160, 128, 32, 32);
    /// <summary>The <c>fighters</c> painting.</summary>
    public static readonly Painting Fighters = new(20, 0, 96, 64, 32);
    /// <summary>The <c>pointer</c> painting.</summary>
    public static readonly Painting Pointer = new(21, 0, 192, 64, 64);
    /// <summary>The <c>pigscene</c> painting.</summary>
    public static readonly Painting Pigscene = new(22, 64, 192, 64, 64);
    /// <summary>The <c>burning_skull</c> painting.</summary>
    public static readonly Painting BurningSkull = new(23, 128, 192, 64, 64);
    /// <summary>The <c>skeleton</c> painting.</summary>
    public static readonly Painting Skeleton = new(24, 192, 64, 64, 48);
    /// <summary>The <c>donkey_kong</c> painting.</summary>
    public static readonly Painting DonkeyKong = new(25, 192, 112, 64, 48);
}
