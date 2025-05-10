namespace Obsidian.WorldData.Features.HeightProviders;

[TreeProperty("minecarft:trapezoid")]
public sealed class TrapezoidHeightProvider : IHeightProvider
{
    public required string Type { get; init; } = "minecarft:trapezoid";

    public int? Absolute { get; init; }
    public int? AboveBottom { get; init; }
    public int? BelowTop { get; init; }

    public required VerticalAnchor MinInclusive { get; init; }
    public required VerticalAnchor MaxInclusive { get; init; }

    public required int Plateau { get; init; }
}
