using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.IntProviders;

/// <summary>
/// Int in <c>[Min, Max]</c> with a flat plateau between two slopes, like vanilla 26.3's trapezoid provider.
/// </summary>
[ConfiguredFeatureProperty(IntProviderTypes.Trapezoid)]
public sealed class TrapezoidIntProvider : IIntProvider
{
    public string Type { get; init; } = IntProviderTypes.Trapezoid;

    public required int Min { get; init; }

    public required int Max { get; init; }

    public required int Plateau { get; init; }

    public int MinValue => this.Min;

    public int MaxValue => this.Max;

    public int Sample(IRandomSource random)
    {
        // Vanilla's symmetric triangle subtracts its draws; the general sum produces a different seeded sequence.
        if (this.Plateau == 0 && this.Max == -this.Min)
        {
            var first = random.NextInt(this.Max + 1);
            var second = random.NextInt(this.Max + 1);

            return first - second;
        }

        var range = this.Max - this.Min;
        if (this.Plateau == range)
        {
            return this.Min + random.NextInt(range + 1);
        }

        var lowerSlopeWidth = (range - this.Plateau) / 2;
        var upperSlopeWidth = range - lowerSlopeWidth;
        var upper = random.NextInt(upperSlopeWidth + 1);
        var lower = random.NextInt(lowerSlopeWidth + 1);

        return this.Min + upper + lower;
    }
}
