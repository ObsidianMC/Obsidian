namespace Obsidian.API.Utilities;

/// <summary>
/// Pairs a value with its relative selection weight.
/// </summary>
public sealed record class Weighted<T>
{
    /// <summary>
    /// Gets the value associated with this weighted entry.
    /// </summary>
    public required T Value { get; init; }

    /// <summary>
    /// Gets the entry's relative selection weight.
    /// </summary>
    public int Weight { get; init; }
}
