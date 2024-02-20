namespace Obsidian.Providers.IntProviders;
public sealed class RangedIntProvider : IIntProvider
{
    public required IntProviderType ProviderType { get; init; }

    public IntProviderRangeValue Value { get; init; }
}
