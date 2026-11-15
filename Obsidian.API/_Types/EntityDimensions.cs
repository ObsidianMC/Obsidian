namespace Obsidian.API;
/// <summary>The size of an entity's hitbox, in blocks.</summary>
public readonly record struct EntityDimension : IEquatable<EntityDimension>
{
    /// <summary>A size of zero.</summary>
    public static readonly EntityDimension Zero = new() { Height = 0, Width = 0 };   

    /// <summary>Gets the hitbox's width along X and Z, in blocks.</summary>
    public required float Width { get; init; }

    /// <summary>Gets the hitbox's height, in blocks.</summary>
    public required float Height { get; init; }

    /// <summary>
    /// Gets the hitbox of an entity standing at <paramref name="position"/>, centered on X and Z with its bottom at Y.
    /// </summary>
    public BoundingBox CreateBBFromPosition(VectorD position)
    {
        var updatedWidth = this.Width / 2.0f;

        return new(new VectorD(position.X - updatedWidth, position.Y, position.Z - updatedWidth),
            new VectorD(position.X + updatedWidth, position.Y + this.Height, position.Z + updatedWidth));
    }

    /// <summary>Indicates whether both sizes have the same width and height.</summary>
    public bool Equals(EntityDimension other) => 
        other.Height == this.Height && other.Width == this.Width;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Height, this.Width);
}
