namespace Obsidian.API;
public readonly record struct EntityDimension : IEquatable<EntityDimension>
{
    public static readonly EntityDimension Zero = new() { Height = 0, Width = 0 };   

    public required float Width { get; init; }

    public required float Height { get; init; }

    public BoundingBox CreateBBFromPosition(VectorF position)
    {
        var updatedWidth = this.Width / 2.0f;

        return new(new VectorF(position.X - updatedWidth, position.Y, position.Z - updatedWidth),
            new VectorF(position.X + updatedWidth, position.Y + this.Height, position.Z + updatedWidth));
    }

    public bool Equals(EntityDimension other) => 
        other.Height == this.Height && other.Width == this.Width;

    public override int GetHashCode() => HashCode.Combine(this.Height, this.Width);
}
