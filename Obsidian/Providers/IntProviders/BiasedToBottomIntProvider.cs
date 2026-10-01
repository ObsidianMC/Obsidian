using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.IntProviders;

/// <summary>
/// Int in <c>[MinInclusive, MaxInclusive]</c> skewed towards the minimum (two nested draws).
/// </summary>
[ConfiguredFeatureProperty(IntProviderTypes.BiasedToBottom)]
public sealed class BiasedToBottomIntProvider : IIntProvider
{
    public string Type { get; init; } = IntProviderTypes.BiasedToBottom;

    public required int MinInclusive { get; init; }

    public required int MaxInclusive { get; init; }

    public int MinValue => this.MinInclusive;

    public int MaxValue => this.MaxInclusive;

    public int Sample(IRandomSource random) =>
        this.MinInclusive + random.NextInt(random.NextInt(this.MaxInclusive - this.MinInclusive + 1) + 1);
}
