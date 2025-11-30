namespace Obsidian.Providers.IntProviders;

[TreeProperty(IntProviderTypes.ClampedNormal)]
public sealed class ClampedNormalIntProvider : IIntProvider
{
    public required string Type { get; init; } = IntProviderTypes.ClampedNormal;

    public IntProviderRangeValue Value { get; init; }

    public float Mean { get; init; }

    public float Deviation { get; init; }

    public int Get()
    {
        // Generate normal distribution using Box-Muller transform
        var u1 = 1.0 - Globals.Random.NextDouble(); // Uniform(0,1]
        var u2 = 1.0 - Globals.Random.NextDouble();
        var randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        var randNormal = this.Mean + this.Deviation * randStdNormal;
        var value = (int)Math.Round(randNormal);
        return Math.Clamp(value, this.Value.MinInclusive, this.Value.MaxInclusive);
    }
}
