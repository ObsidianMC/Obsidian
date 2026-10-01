using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Providers;

namespace Obsidian.WorldData.Features.HeightProviders;

/// <summary>
/// Picks one of several height providers by weight, then samples it.
/// </summary>
[ConfiguredFeatureProperty("minecraft:weighted_list")]
public sealed class WeightedListHeightProvider : IHeightProvider
{
    public string Type { get; init; } = "minecraft:weighted_list";

    public required WeightedEntry<IHeightProvider>[] Distribution { get; init; }

    public int Sample(IRandomSource random, WorldGenerationContext context) =>
        WeightedEntry<IHeightProvider>.Pick(this.Distribution, random).Sample(random, context);
}
