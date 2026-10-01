namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// Inclusive block box around a placed tree, like vanilla's <c>BoundingBox</c>.
/// </summary>
internal readonly record struct TreeBounds(Vector Min, Vector Max)
{
    public int SizeX => this.Max.X - this.Min.X + 1;

    public int SizeY => this.Max.Y - this.Min.Y + 1;

    public int SizeZ => this.Max.Z - this.Min.Z + 1;

    public bool IsInside(Vector position) =>
        position.X >= this.Min.X && position.X <= this.Max.X &&
        position.Z >= this.Min.Z && position.Z <= this.Max.Z &&
        position.Y >= this.Min.Y && position.Y <= this.Max.Y;

    /// <summary>The smallest box containing every position; the sets must not all be empty.</summary>
    public static TreeBounds Encapsulating(params VanillaBlockPosSet[] sets)
    {
        var min = new Vector(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Vector(int.MinValue, int.MinValue, int.MinValue);
        foreach (var set in sets)
        {
            foreach (var position in set)
            {
                min = new Vector(Math.Min(min.X, position.X), Math.Min(min.Y, position.Y), Math.Min(min.Z, position.Z));
                max = new Vector(Math.Max(max.X, position.X), Math.Max(max.Y, position.Y), Math.Max(max.Z, position.Z));
            }
        }

        return new TreeBounds(min, max);
    }
}
