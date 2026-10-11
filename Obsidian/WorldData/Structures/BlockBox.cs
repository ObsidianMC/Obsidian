namespace Obsidian.WorldData.Structures;

/// <summary>
/// Inclusive block box, like vanilla's <c>BoundingBox</c>.
/// </summary>
public readonly record struct BlockBox(Vector Min, Vector Max)
{
    public int MinX => this.Min.X;

    public int MinY => this.Min.Y;

    public int MinZ => this.Min.Z;

    public int MaxX => this.Max.X;

    public int MaxY => this.Max.Y;

    public int MaxZ => this.Max.Z;

    public int XSpan => this.Max.X - this.Min.X + 1;

    public int YSpan => this.Max.Y - this.Min.Y + 1;

    public int ZSpan => this.Max.Z - this.Min.Z + 1;

    /// <summary>Vanilla <c>getCenter</c>: rounds towards the max corner.</summary>
    public Vector Center => new(this.Min.X + this.XSpan / 2, this.Min.Y + this.YSpan / 2, this.Min.Z + this.ZSpan / 2);

    /// <summary>
    /// The box between two corners, like vanilla's constructor (inverted bounds are swapped).
    /// </summary>
    public static BlockBox Create(int minX, int minY, int minZ, int maxX, int maxY, int maxZ) => new(
        new Vector(Math.Min(minX, maxX), Math.Min(minY, maxY), Math.Min(minZ, maxZ)),
        new Vector(Math.Max(minX, maxX), Math.Max(minY, maxY), Math.Max(minZ, maxZ)));

    /// <summary>The box spanning two opposite corners given in any order.</summary>
    public static BlockBox FromCorners(Vector a, Vector b) => Create(a.X, a.Y, a.Z, b.X, b.Y, b.Z);

    /// <summary>
    /// Vanilla <c>BoundingBox.orientBox</c>: a box of the given size at an offset from (<paramref name="x"/>,
    /// <paramref name="y"/>, <paramref name="z"/>), extending in <paramref name="direction"/> (width across it, depth along it).
    /// </summary>
    public static BlockBox Orient(int x, int y, int z, int offsetX, int offsetY, int offsetZ, int width, int height, int depth,
        BlockFace direction) =>
        direction switch
        {
            BlockFace.North => Create(x + offsetX, y + offsetY, z - depth + 1 + offsetZ,
                x + width - 1 + offsetX, y + height - 1 + offsetY, z + offsetZ),
            BlockFace.West => Create(x - depth + 1 + offsetZ, y + offsetY, z + offsetX,
                x + offsetZ, y + height - 1 + offsetY, z + width - 1 + offsetX),
            BlockFace.East => Create(x + offsetZ, y + offsetY, z + offsetX,
                x + depth - 1 + offsetZ, y + height - 1 + offsetY, z + width - 1 + offsetX),
            _ => Create(x + offsetX, y + offsetY, z + offsetZ,
                x + width - 1 + offsetX, y + height - 1 + offsetY, z + depth - 1 + offsetZ)
        };

    /// <summary>The smallest box containing every box in <paramref name="boxes"/>, or <c>null</c> when there are none.</summary>
    public static BlockBox? Encapsulating(IEnumerable<BlockBox> boxes)
    {
        BlockBox? result = null;
        foreach (var box in boxes)
            result = result is null ? box : result.Value.Encapsulate(box);

        return result;
    }

    public BlockBox Move(Vector offset) => new(this.Min + offset, this.Max + offset);

    public BlockBox Move(int x, int y, int z) => this.Move(new Vector(x, y, z));

    /// <summary>Vanilla <c>inflatedBy</c>.</summary>
    public BlockBox InflatedBy(int amount) => this.InflatedBy(amount, amount, amount);

    public BlockBox InflatedBy(int x, int y, int z) => new(this.Min - new Vector(x, y, z), this.Max + new Vector(x, y, z));

    /// <summary>The smallest box containing this box and <paramref name="other"/>.</summary>
    public BlockBox Encapsulate(BlockBox other) => new(
        new Vector(Math.Min(this.Min.X, other.Min.X), Math.Min(this.Min.Y, other.Min.Y), Math.Min(this.Min.Z, other.Min.Z)),
        new Vector(Math.Max(this.Max.X, other.Max.X), Math.Max(this.Max.Y, other.Max.Y), Math.Max(this.Max.Z, other.Max.Z)));

    /// <summary>The smallest box containing this box and <paramref name="position"/>.</summary>
    public BlockBox Encapsulate(Vector position) => this.Encapsulate(new BlockBox(position, position));

    public bool Intersects(BlockBox other) =>
        this.Max.X >= other.Min.X && this.Min.X <= other.Max.X &&
        this.Max.Z >= other.Min.Z && this.Min.Z <= other.Max.Z &&
        this.Max.Y >= other.Min.Y && this.Min.Y <= other.Max.Y;

    /// <summary>Whether the box overlaps the horizontal area, ignoring Y.</summary>
    public bool Intersects(int minX, int minZ, int maxX, int maxZ) =>
        this.Max.X >= minX && this.Min.X <= maxX && this.Max.Z >= minZ && this.Min.Z <= maxZ;

    public bool IsInside(Vector position) => this.IsInside(position.X, position.Y, position.Z);

    public bool IsInside(int x, int y, int z) =>
        x >= this.Min.X && x <= this.Max.X &&
        z >= this.Min.Z && z <= this.Max.Z &&
        y >= this.Min.Y && y <= this.Max.Y;

    /// <summary>The 8 corners in vanilla's <c>forAllCorners</c> order.</summary>
    public IEnumerable<Vector> Corners()
    {
        yield return new Vector(this.Max.X, this.Max.Y, this.Max.Z);
        yield return new Vector(this.Min.X, this.Max.Y, this.Max.Z);
        yield return new Vector(this.Max.X, this.Min.Y, this.Max.Z);
        yield return new Vector(this.Min.X, this.Min.Y, this.Max.Z);
        yield return new Vector(this.Max.X, this.Max.Y, this.Min.Z);
        yield return new Vector(this.Min.X, this.Max.Y, this.Min.Z);
        yield return new Vector(this.Max.X, this.Min.Y, this.Min.Z);
        yield return new Vector(this.Min.X, this.Min.Y, this.Min.Z);
    }
}
