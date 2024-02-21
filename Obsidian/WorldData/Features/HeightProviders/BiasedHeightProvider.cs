namespace Obsidian.WorldData.Features.HeightProviders;

[TreeProperty("minecraft:biased_to_bottom")]
[TreeProperty("minecraft:very_biased_to_bottom")]
public sealed class BiasedHeightProvider : IHeightProvider
{
    /// <summary>
    /// Can be minecraft:biased_to_bottom or minecraft:very_biased_to_bottom.
    /// </summary>
    public required string Type { get; init; }

    public int? Absolute { get; init; }
    public int? AboveBottom { get; init; }
    public int? BelowTop { get; init; }

    public required VerticalAnchor MinInclusive { get; init; }
    public required VerticalAnchor MaxInclusive { get; init; }

    public int Inner { get; init; } = 1;
}
