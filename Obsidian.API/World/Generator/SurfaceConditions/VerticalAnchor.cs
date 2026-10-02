namespace Obsidian.API.World.Generator.SurfaceConditions;

public readonly record struct VerticalAnchor
{
    public int? Absolute { get; init; }

    public int? AboveBottom { get; init; }

    public int? BelowTop { get; init; }
}
