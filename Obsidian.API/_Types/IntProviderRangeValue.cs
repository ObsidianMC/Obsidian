namespace Obsidian.API;
public readonly struct IntProviderRangeValue
{
    public required int MinInclusive { get; init; }

    public required int MaxInclusive { get; init; }

    public float? Mean { get; init; }

    public float? Deviation { get; init; }
}
