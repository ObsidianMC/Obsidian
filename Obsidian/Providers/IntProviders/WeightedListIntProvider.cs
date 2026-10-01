using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.IntProviders;

/// <summary>
/// Picks one of several int providers by weight, then samples it.
/// </summary>
[ConfiguredFeatureProperty(IntProviderTypes.WeightedList)]
public sealed class WeightedListIntProvider : IIntProvider
{
    public string Type { get; init; } = IntProviderTypes.WeightedList;

    public required WeightedEntry<IIntProvider>[] Distribution { get; init; }

    public int MinValue => this.Distribution.Min(entry => entry.Data.MinValue);

    public int MaxValue => this.Distribution.Max(entry => entry.Data.MaxValue);

    public int Sample(IRandomSource random) => WeightedEntry<IIntProvider>.Pick(this.Distribution, random).Sample(random);
}
