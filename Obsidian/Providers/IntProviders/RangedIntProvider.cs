namespace Obsidian.Providers.IntProviders;

[ConfiguredFeatureProperty(IntProviderTypes.Uniform)]
[ConfiguredFeatureProperty(IntProviderTypes.BiasedToBottom)]
public sealed class RangedIntProvider : IIntProvider
{
    public required string Type { get; init; }

    public required int MinInclusive { get; init; }

    public required int MaxInclusive { get; init; }

    public int Get()
    {
        if (this.Type == IntProviderTypes.BiasedToBottom)
        {
            // Biased towards the minimum value
            var range = this.MaxInclusive - this.MinInclusive;
            var rand1 = Globals.Random.Next(range + 1);
            var rand2 = Globals.Random.Next(range + 1);
            return this.MinInclusive + Math.Min(rand1, rand2);
        }

        // Uniform distribution
        return Globals.Random.Next(this.MinInclusive, this.MaxInclusive + 1);
    }
}
