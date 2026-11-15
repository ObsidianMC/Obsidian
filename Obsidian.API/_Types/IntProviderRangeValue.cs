namespace Obsidian.API;
/// <summary>
/// An inclusive range of integers, with an optional mean and deviation for a normal distribution over it.
/// </summary>
public readonly record struct IntProviderRangeValue
{
    /// <summary>Gets the lowest value in the range.</summary>
    public required int MinInclusive { get; init; }

    /// <summary>Gets the highest value in the range.</summary>
    public required int MaxInclusive { get; init; }

    /// <summary>Gets the distribution's mean, or null if it has none.</summary>
    public float? Mean { get; init; }

    /// <summary>Gets the distribution's standard deviation, or null if it has none.</summary>
    public float? Deviation { get; init; }

    /// <summary>Splits the value into its range, mean and deviation.</summary>
    public void Deconstruct(out int minInclusive, out int maxInclusive, 
        out float? mean, out float? deviation)
    {
        minInclusive = this.MinInclusive;
        maxInclusive = this.MaxInclusive;
        mean = this.Mean;
        deviation = this.Deviation;
    }

    /// <summary>Splits the value into its range.</summary>
    public void Deconstruct(out int minInclusive, out int maxInclusive)
    {
        minInclusive = this.MinInclusive;
        maxInclusive = this.MaxInclusive;
    }
}
