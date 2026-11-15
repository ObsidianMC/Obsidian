namespace Obsidian.API.World.Generator.SurfaceConditions;

/// <summary>
/// Specifies a world Y directly or as an offset from the dimension's vertical bounds.
/// </summary>
public readonly record struct VerticalAnchor
{
    /// <summary>
    /// Gets the absolute world Y in blocks, or <c>null</c> when another anchor form is used.
    /// </summary>
    public int? Absolute { get; init; }

    /// <summary>
    /// Gets the block offset above the minimum build Y, or <c>null</c> when unused.
    /// </summary>
    public int? AboveBottom { get; init; }

    /// <summary>
    /// Gets the block offset below the highest build Y, or <c>null</c> when unused.
    /// </summary>
    public int? BelowTop { get; init; }
}
