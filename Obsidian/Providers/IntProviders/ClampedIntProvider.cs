namespace Obsidian.Providers.IntProviders;

[ConfiguredFeatureProperty(IntProviderTypes.Clamped)]
public sealed class ClampedIntProvider : IIntProvider
{
    public required string Type { get; init; } = IntProviderTypes.Clamped;

    public IntProviderRangeValue Value { get; init; }

    public IIntProvider Source { get; init; } = default!;

    public int Get()
    {
        var sourceValue = this.Source.Get();
        return Math.Clamp(sourceValue, this.Value.MinInclusive, this.Value.MaxInclusive);
    }
}
