using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.IntProviders;

/// <summary>
/// Uniform int in <c>[MinInclusive, MaxInclusive]</c>.
/// </summary>
[ConfiguredFeatureProperty(IntProviderTypes.Uniform)]
public sealed class UniformIntProvider : IIntProvider
{
    public string Type { get; init; } = IntProviderTypes.Uniform;

    public required int MinInclusive { get; init; }

    public required int MaxInclusive { get; init; }

    public int MinValue => this.MinInclusive;

    public int MaxValue => this.MaxInclusive;

    public int Sample(IRandomSource random) => random.NextInt(this.MaxInclusive - this.MinInclusive + 1) + this.MinInclusive;
}
