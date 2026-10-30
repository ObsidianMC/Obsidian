using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.IntProviders;

/// <summary>
/// Int in <c>[MinInclusive, MaxInclusive]</c> skewed towards the minimum with three nested draws.
/// </summary>
[ConfiguredFeatureProperty(IntProviderTypes.VeryBiasedToBottom)]
public sealed class VeryBiasedToBottomIntProvider : IIntProvider
{
    public string Type { get; init; } = IntProviderTypes.VeryBiasedToBottom;

    public required int MinInclusive { get; init; }

    public required int MaxInclusive { get; init; }

    public int MinValue => this.MinInclusive;

    public int MaxValue => this.MaxInclusive;

    public int Sample(IRandomSource random)
    {
        var range = this.MaxInclusive - this.MinInclusive + 1;
        var first = random.NextInt(range);
        var second = random.NextInt(first + 1);
        var third = random.NextInt(second + 1);

        return this.MinInclusive + third;
    }
}
