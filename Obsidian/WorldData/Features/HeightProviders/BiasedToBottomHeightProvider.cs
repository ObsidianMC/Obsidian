using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.HeightProviders;

/// <summary>
/// Y in <c>[MinInclusive, MaxInclusive]</c> skewed towards the bottom.
/// </summary>
[ConfiguredFeatureProperty("minecraft:biased_to_bottom")]
public sealed class BiasedToBottomHeightProvider : IHeightProvider
{
    public string Type { get; init; } = "minecraft:biased_to_bottom";

    public required VerticalAnchor MinInclusive { get; init; }

    public required VerticalAnchor MaxInclusive { get; init; }

    public int Inner { get; init; } = 1;

    public int Sample(IRandomSource random, WorldGenerationContext context)
    {
        var min = this.MinInclusive.Resolve(context);
        var max = this.MaxInclusive.Resolve(context);

        if (max - min - this.Inner + 1 <= 0)
            return min;

        var bound = random.NextInt(max - min - this.Inner + 1);
        return random.NextInt(bound + this.Inner) + min;
    }
}
