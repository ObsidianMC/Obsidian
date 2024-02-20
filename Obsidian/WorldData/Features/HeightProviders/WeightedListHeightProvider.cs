namespace Obsidian.WorldData.Features.HeightProviders;

[TreeProperty("minecarft:weighted_list")]
public sealed class WeightedListHeightProvider : IHeightProvider
{
    public required string Type { get; init; } = "minecarft:weighted_list";

    public int? Absolute { get; init; }
    public int? AboveBottom { get; init; }
    public int? BelowTop { get; init; }

    public List<Entry> Distribution { get; } = [];

    public readonly struct Entry
    {
        public required IHeightProvider Data { get; init; }

        public required int Weight { get; init; }
    }
}
