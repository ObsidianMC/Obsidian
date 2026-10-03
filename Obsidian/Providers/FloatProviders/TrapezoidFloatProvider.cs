using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.FloatProviders;

/// <summary>
/// Float in <c>[Min, Max]</c> with a flat middle of width <c>Plateau</c> (sum of two uniform draws).
/// </summary>
[ConfiguredFeatureProperty("minecraft:trapezoid")]
public sealed class TrapezoidFloatProvider : IFloatProvider
{
    public string Type { get; init; } = "minecraft:trapezoid";

    public required float Min { get; init; }

    public required float Max { get; init; }

    public required float Plateau { get; init; }

    public float MinValue => this.Min;

    public float MaxValue => this.Max;

    public float Sample(IRandomSource random)
    {
        var range = this.Max - this.Min;
        var slope = (range - this.Plateau) / 2.0f;
        var rest = range - slope;
        return this.Min + random.NextFloat() * rest + random.NextFloat() * slope;
    }
}
