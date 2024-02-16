namespace Obsidian.API.World.Features;
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
}
