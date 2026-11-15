namespace Obsidian.API.World.Features;

/// <summary>
/// A Y level that is absolute or relative to the bottom or top of the generated area.
/// </summary>
public readonly record struct VerticalAnchor
{
    /// <summary>
    /// Gets the absolute world Y in blocks, or <c>null</c> for a relative anchor.
    /// </summary>
    public int? Absolute { get; private init; }

    /// <summary>
    /// Gets the offset above the minimum generation Y in blocks, or <c>null</c> when unused.
    /// </summary>
    public int? AboveBottom { get; private init; }

    /// <summary>
    /// Gets the offset below the highest generation Y in blocks, or <c>null</c> when unused.
    /// </summary>
    public int? BelowTop { get; private init; }

    /// <summary>
    /// Creates an anchor at an absolute world Y in blocks.
    /// </summary>
    public static VerticalAnchor WithAbsolute(int absolute) => new()
    {
        Absolute = absolute,
    };

    /// <summary>
    /// Creates an anchor a number of blocks above the minimum generation Y.
    /// </summary>
    public static VerticalAnchor WithAboveBottom(int aboveBottom) => new()
    {
        AboveBottom = aboveBottom
    };

    /// <summary>
    /// Creates an anchor a number of blocks below the highest generation Y.
    /// </summary>
    public static VerticalAnchor WithBelowTop(int belowTop) => new()
    {
        BelowTop = belowTop
    };

    /// <summary>
    /// Gets an anchor at the minimum generation Y.
    /// </summary>
    public static VerticalAnchor Bottom => WithAboveBottom(0);

    /// <summary>
    /// Gets an anchor at the highest generation Y.
    /// </summary>
    public static VerticalAnchor Top => WithBelowTop(0);

    /// <summary>
    /// Resolves the anchor to a block Y, like vanilla's VerticalAnchor.resolveY.
    /// </summary>
    public int Resolve(WorldGenerationContext context)
    {
        if (this.Absolute is int absolute)
            return absolute;

        if (this.AboveBottom is int aboveBottom)
            return context.MinY + aboveBottom;

        return context.MinY + context.Height - 1 - (this.BelowTop ?? 0);
    }
}
