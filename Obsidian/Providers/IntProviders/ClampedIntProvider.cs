namespace Obsidian.Providers.IntProviders;
public sealed class ClampedIntProvider : IIntProvider
{
    public IntProviderType ProviderType => IntProviderType.Clamped;

    public IntProviderRangeValue Value { get; init; }

    public IIntProvider Source { get; init; } = default!;
}
