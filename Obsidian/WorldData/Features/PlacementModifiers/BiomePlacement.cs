using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Keeps the position only if its biome lists the feature being decorated.
/// </summary>
[ConfiguredFeatureProperty("minecraft:biome")]
public sealed class BiomePlacement : PlacementFilterBase
{
    public override string Type => "minecraft:biome";

    protected override bool ShouldPlace(PlacementContext context, IRandomSource random, Vector position)
    {
        var feature = context.TopFeature
            ?? throw new InvalidOperationException("Tried to biome check a feature that isn't placed with a biome check.");

        return context.BiomeHasFeature!(context.Level.GetBiome(position), feature);
    }
}
