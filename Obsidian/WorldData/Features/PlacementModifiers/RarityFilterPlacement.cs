using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Keeps the position on average once every <see cref="Chance"/> attempts.
/// </summary>
[ConfiguredFeatureProperty("minecraft:rarity_filter")]
public sealed class RarityFilterPlacement : PlacementFilterBase
{
    public override string Type => "minecraft:rarity_filter";

    public required int Chance { get; init; }

    protected override bool ShouldPlace(PlacementContext context, IRandomSource random, Vector position) =>
        random.NextFloat() < 1.0f / this.Chance;
}
