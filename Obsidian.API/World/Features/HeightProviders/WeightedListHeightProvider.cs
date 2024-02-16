namespace Obsidian.API.World.Features.HeightProviders;
public sealed class WeightedListHeightProvider : IHeightProvider
{
    public string Type => "minecarft:weighted_list";

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
