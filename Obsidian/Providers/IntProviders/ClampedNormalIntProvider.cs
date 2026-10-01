using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.IntProviders;

/// <summary>
/// Normally distributed int (<c>Mean</c>, <c>Deviation</c>) clamped to <c>[MinInclusive, MaxInclusive]</c>.
/// </summary>
[ConfiguredFeatureProperty(IntProviderTypes.ClampedNormal)]
public sealed class ClampedNormalIntProvider : IIntProvider
{
    public string Type { get; init; } = IntProviderTypes.ClampedNormal;

    public required float Mean { get; init; }

    public required float Deviation { get; init; }

    public required int MinInclusive { get; init; }

    public required int MaxInclusive { get; init; }

    public int MinValue => this.MinInclusive;

    public int MaxValue => this.MaxInclusive;

    // Vanilla clamps the float normal sample to float bounds, then truncates.
    public int Sample(IRandomSource random)
    {
        var normal = this.Mean + (float)random.NextGaussian() * this.Deviation;
        return (int)Math.Clamp(normal, (float)this.MinInclusive, (float)this.MaxInclusive);
    }
}
