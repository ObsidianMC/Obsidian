namespace Obsidian.WorldData.Structures;

/// <summary>
/// Inclusive block box, like vanilla's <c>BoundingBox</c>.
/// </summary>
public readonly record struct BlockBox(Vector Min, Vector Max)
{
    /// <summary>The box spanning two opposite corners given in any order.</summary>
    public static BlockBox FromCorners(Vector a, Vector b) => new(
        new Vector(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)),
        new Vector(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)));

    public BlockBox Move(Vector offset) => new(this.Min + offset, this.Max + offset);

    public bool IsInside(Vector position) =>
        position.X >= this.Min.X && position.X <= this.Max.X &&
        position.Z >= this.Min.Z && position.Z <= this.Max.Z &&
        position.Y >= this.Min.Y && position.Y <= this.Max.Y;

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
