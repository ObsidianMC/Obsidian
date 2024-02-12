namespace Obsidian.Providers.IntProviders;
public sealed class ClampedNormalIntProvider : IIntProvider
{
    public IntProviderType ProviderType => IntProviderType.ClampedNormal;

    public IntProviderRangeValue Value { get; init; }
}
