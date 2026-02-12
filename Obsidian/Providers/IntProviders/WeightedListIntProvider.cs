namespace Obsidian.Providers.IntProviders;

[ConfiguredFeatureProperty(IntProviderTypes.WeightedList)]
public sealed class WeightedListIntProvider : IIntProvider
{
    public required string Type { get; init; } = IntProviderTypes.WeightedList;

    public List<WeightedListValue> Distribution { get; set; } = [];

    public int Get()
    {
        if (this.Distribution.Count == 0)
            return 0;

        // Calculate total weight
        var totalWeight = this.Distribution.Sum(d => d.Weight);

        // Pick a random value based on weights
        var randomValue = Globals.Random.Next(totalWeight);
        var currentWeight = 0;

        foreach (var entry in this.Distribution)
        {
            currentWeight += entry.Weight;
            if (randomValue < currentWeight)
            {
                return entry.Data.Get();
            }
        }

        // Fallback to first entry
        return this.Distribution[0].Data.Get();
    }

    public readonly struct WeightedListValue
    {
        public required IIntProvider Data { get; init; }

        public required int Weight { get; init; }
    }
}
