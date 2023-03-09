namespace Obsidian.API;
public readonly struct EntityDimension
{
    public required float Width { get; init; }

    public required float Height { get; init; }

    public BoundingBox CreateBBFromPosition(VectorF position)
    {
        var updatedWidth = this.Width / 2.0f;

        return new(new VectorF(position.X - updatedWidth, position.Y, position.Z - updatedWidth),
            new VectorF(position.X + updatedWidth, position.Y + this.Height, position.Z + updatedWidth));
    }
}
