namespace Obsidian.API.Utilities;

public sealed record class Weighted<T>
{
    public required T Value { get; init; }

    public int Weight { get; init; }
}
