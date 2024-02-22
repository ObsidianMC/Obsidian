namespace Obsidian.Providers.IntProviders;

[TreeProperty(IntProviderTypes.WeightedList)]
public sealed class WeightedListIntProvider : IIntProvider
{
    public required string Type { get; init; } = IntProviderTypes.WeightedList;

    public List<WeightedListValue> Distribution { get; set; } = [];

    public readonly struct WeightedListValue
    {
        public required IIntProvider Data { get; init; }

        public required int Weight { get; init; }
    }
}
