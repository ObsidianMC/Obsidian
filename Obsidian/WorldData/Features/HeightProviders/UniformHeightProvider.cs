namespace Obsidian.WorldData.Features.HeightProviders;

[TreeProperty("minecarft:uniform")]
public sealed class UniformHeightProvider : IHeightProvider
{
    public required string Type { get; init; } = "minecarft:uniform";

    public int? Absolute { get; init; }
    public int? AboveBottom { get; init; }
    public int? BelowTop { get; init; }

    public required VerticalAnchor MinInclusive { get; init; }
    public required VerticalAnchor MaxInclusive { get; init; }
}
