using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.IntProviders;

/// <summary>
/// Another provider's value clamped to <c>[MinInclusive, MaxInclusive]</c>.
/// </summary>
[ConfiguredFeatureProperty(IntProviderTypes.Clamped)]
public sealed class ClampedIntProvider : IIntProvider
{
    public string Type { get; init; } = IntProviderTypes.Clamped;

    public required IIntProvider Source { get; init; }

    public required int MinInclusive { get; init; }

    public required int MaxInclusive { get; init; }

    public int MinValue => Math.Max(this.MinInclusive, this.Source.MinValue);

    public int MaxValue => Math.Min(this.MaxInclusive, this.Source.MaxValue);

    public int Sample(IRandomSource random) => Math.Clamp(this.Source.Sample(random), this.MinInclusive, this.MaxInclusive);
}
