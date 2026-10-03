using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.HeightProviders;

/// <summary>
/// Y in <c>[MinInclusive, MaxInclusive]</c> with a flat middle of width <c>Plateau</c>.
/// </summary>
[ConfiguredFeatureProperty("minecraft:trapezoid")]
public sealed class TrapezoidHeightProvider : IHeightProvider
{
    public string Type { get; init; } = "minecraft:trapezoid";

    public required VerticalAnchor MinInclusive { get; init; }

    public required VerticalAnchor MaxInclusive { get; init; }

    public int Plateau { get; init; }

    public int Sample(IRandomSource random, WorldGenerationContext context)
    {
        var min = this.MinInclusive.Resolve(context);
        var max = this.MaxInclusive.Resolve(context);

        if (min > max)
            return min;

        var range = max - min;
        if (this.Plateau >= range)
            return random.NextInt(range + 1) + min;

        var slope = (range - this.Plateau) / 2;
        var rest = range - slope;
        return min + random.NextInt(rest + 1) + random.NextInt(slope + 1);
    }
}
