namespace Obsidian.Providers.IntProviders;
public sealed class RangedIntProvider : IIntProvider
{
    public IntProviderType ProviderType => IntProviderType.Uniform | IntProviderType.BiasedToBottom;

    public IntProviderRangeValue Value { get; init; }
}
