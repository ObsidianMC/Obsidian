namespace Obsidian.API.World;
public readonly struct SplinePoint
{
    public required double Derivative { get; init; }
    public required double Location { get; init; }

    public required ISpline Value { get; init; }
}

