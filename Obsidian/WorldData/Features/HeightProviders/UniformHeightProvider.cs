using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.HeightProviders;

/// <summary>
/// Uniform Y in <c>[MinInclusive, MaxInclusive]</c>.
/// </summary>
[ConfiguredFeatureProperty("minecraft:uniform")]
public sealed class UniformHeightProvider : IHeightProvider
{
    public string Type { get; init; } = "minecraft:uniform";

    public required VerticalAnchor MinInclusive { get; init; }

    public required VerticalAnchor MaxInclusive { get; init; }

    public int Sample(IRandomSource random, WorldGenerationContext context)
    {
        var min = this.MinInclusive.Resolve(context);
        var max = this.MaxInclusive.Resolve(context);

        // Vanilla returns the minimum for an empty range.
        return min > max ? min : random.NextInt(max - min + 1) + min;
    }
}
