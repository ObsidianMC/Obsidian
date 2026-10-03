namespace Obsidian.API.World;
public readonly record struct SplinePoint
{
    public required double Derivative { get; init; }
    public required double Location { get; init; }

    public required ISpline Value { get; init; }
}
