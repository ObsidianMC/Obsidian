namespace Obsidian.API.World.Features.HeightProviders;
public sealed class TrapezoidHeightProvider : IHeightProvider
{
    public string Type => "minecarft:trapezoid";

    public int? Absolute { get; init; }
    public int? AboveBottom { get; init; }
    public int? BelowTop { get; init; }

    public required VerticalAnchor MinInclusive { get; init; }
    public required VerticalAnchor MaxInclusive { get; init; }

    public required int Plateau { get; init; }
}
