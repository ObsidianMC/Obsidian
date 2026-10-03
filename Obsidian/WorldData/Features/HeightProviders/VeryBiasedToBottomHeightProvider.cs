using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.HeightProviders;

/// <summary>
/// Y in <c>[MinInclusive, MaxInclusive]</c> strongly skewed towards the bottom (three nested draws).
/// </summary>
[ConfiguredFeatureProperty("minecraft:very_biased_to_bottom")]
public sealed class VeryBiasedToBottomHeightProvider : IHeightProvider
{
    public string Type { get; init; } = "minecraft:very_biased_to_bottom";

    public required VerticalAnchor MinInclusive { get; init; }

    public required VerticalAnchor MaxInclusive { get; init; }

    public int Inner { get; init; } = 1;

    public int Sample(IRandomSource random, WorldGenerationContext context)
    {
        var min = this.MinInclusive.Resolve(context);
        var max = this.MaxInclusive.Resolve(context);

        if (max - min - this.Inner + 1 <= 0)
            return min;

        var first = NextInt(random, min + this.Inner, max);
        var second = NextInt(random, min, first - 1);
        return NextInt(random, min, second - 1 + this.Inner);
    }

    // Vanilla Mth.nextInt: inclusive range, collapsing to the minimum when empty.
    private static int NextInt(IRandomSource random, int min, int max) => min >= max ? min : random.NextInt(max - min + 1) + min;
}
