using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Sets the position's Y from a height provider.
/// </summary>
[ConfiguredFeatureProperty("minecraft:height_range")]
public sealed class HeightRangePlacement : SinglePlacementModifierBase
{
    public override string Type => "minecraft:height_range";

    public required IHeightProvider Height { get; init; }

    public override Vector? GetPosition(PlacementContext context, IRandomSource random, Vector position) =>
        new Vector(position.X, this.Height.Sample(random, context.Generation), position.Z);
}
