namespace Obsidian.API;
public readonly struct IntProviderRangeValue
{
    public required int MinInclusive { get; init; }

    public required int MaxInclusive { get; init; }

    public float? Mean { get; init; }

    public float? Deviation { get; init; }

    public void Deconstruct(out int minInclusive, out int maxInclusive, 
        out float? mean, out float? deviation)
    {
        minInclusive = this.MinInclusive;
        maxInclusive = this.MaxInclusive;
        mean = this.Mean;
        deviation = this.Deviation;
    }

    public void Deconstruct(out int minInclusive, out int maxInclusive)
    {
        minInclusive = this.MinInclusive;
        maxInclusive = this.MaxInclusive;
    }
}
