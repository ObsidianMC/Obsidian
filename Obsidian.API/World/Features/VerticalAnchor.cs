namespace Obsidian.API.World.Features;

/// <summary>
/// A Y level that is absolute or relative to the bottom or top of the generated area.
/// </summary>
public readonly record struct VerticalAnchor
{
    public int? Absolute { get; private init; }

    public int? AboveBottom { get; private init; }

    public int? BelowTop { get; private init; }

    public static VerticalAnchor WithAbsolute(int absolute) => new()
    {
        Absolute = absolute,
    };

    public static VerticalAnchor WithAboveBottom(int aboveBottom) => new()
    {
        AboveBottom = aboveBottom
    };

    public static VerticalAnchor WithBelowTop(int belowTop) => new()
    {
        BelowTop = belowTop
    };

    public static VerticalAnchor Bottom => WithAboveBottom(0);

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
