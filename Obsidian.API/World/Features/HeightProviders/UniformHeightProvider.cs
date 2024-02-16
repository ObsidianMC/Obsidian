namespace Obsidian.API.World.Features.HeightProviders;
public sealed class UniformHeightProvider : IHeightProvider
{
    public string Type => "minecarft:uniform";

    public int? Absolute { get; init; }
    public int? AboveBottom { get; init; }
    public int? BelowTop { get; init; }

    public required VerticalAnchor MinInclusive { get; init; }
    public required VerticalAnchor MaxInclusive { get; init; }
}
