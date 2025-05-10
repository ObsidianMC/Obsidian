namespace Obsidian.Providers.IntProviders;

[TreeProperty(IntProviderTypes.WeightedList)]
public sealed class WeightedListIntProvider : IIntProvider
{
    public required string Type { get; init; } = IntProviderTypes.WeightedList;

    public List<WeightedListValue> Distribution { get; set; } = [];

    //TODO
    public int Get() => 0;

    public readonly struct WeightedListValue
    {
        public required IIntProvider Data { get; init; }

        public required int Weight { get; init; }
    }
}
