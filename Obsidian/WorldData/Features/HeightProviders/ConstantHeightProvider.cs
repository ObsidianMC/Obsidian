namespace Obsidian.WorldData.Features.HeightProviders;

[ConfiguredFeatureProperty("minecarft:constant")]
public sealed class ConstantHeightProvider : IHeightProvider
{
    public required string Type { get; init; } = "minecarft:constant";

    public int? Absolute { get; init; }
    public int? AboveBottom { get; init; }
    public int? BelowTop { get; init; }

    public required VerticalAnchor Value { get; init; }
}
