namespace Obsidian.Providers.IntProviders;
public sealed class WeightedListIntProvider : IIntProvider
{
    public IntProviderType ProviderType => IntProviderType.WeightedList;

    public List<WeightedListValue> Distribution { get; } = [];

    public readonly struct WeightedListValue
    {
        public required IIntProvider Data { get; init; }

        public required int Weight { get; init; }
    }
}
