using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Repeats the position <see cref="Count"/> times (sampled once).
/// </summary>
[ConfiguredFeatureProperty("minecraft:count")]
public sealed class CountPlacement : PlacementModifierBase
{
    public override string Type => "minecraft:count";

    public required IIntProvider Count { get; init; }

    public override IEnumerable<Vector> GetPositions(PlacementContext context, IRandomSource random, Vector position) =>
        Enumerable.Repeat(position, this.Count.Sample(random));
}
