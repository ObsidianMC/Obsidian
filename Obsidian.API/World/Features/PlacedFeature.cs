using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Features;

/// <summary>
/// A configured feature plus the placement modifiers that decide where and how often it's placed,
/// like vanilla's PlacedFeature.
/// </summary>
public sealed class PlacedFeature : IFeature
{
    /// <summary>
    /// Registry id (e.g. <c>minecraft:trees_plains</c>), or empty for placed features defined inline.
    /// </summary>
    public string Identifier { get; init; } = string.Empty;

    public required ConfiguredFeatureBase Feature { get; init; }

    public PlacementModifierBase[] Placement { get; init; } = [];

    /// <summary>
    /// Runs the placement modifiers from <paramref name="origin"/> and places the feature at every resulting position.
    /// </summary>
    public bool Place(IWorldGenLevel level, WorldGenerationContext generation, IRandomSource random, Vector origin) =>
        this.PlaceWithContext(new PlacementContext { Level = level, Generation = generation }, random, origin);

    /// <summary>
    /// Like <see cref="Place"/>, but marks this feature as the top feature so <c>minecraft:biome</c> filters keep only
    /// positions whose biome lists it (<paramref name="biomeHasFeature"/>).
    /// </summary>
    public bool PlaceWithBiomeCheck(IWorldGenLevel level, WorldGenerationContext generation, IRandomSource random, Vector origin,
        Func<BiomeCodec, PlacedFeature, bool> biomeHasFeature) =>
        this.PlaceWithContext(new PlacementContext { Level = level, Generation = generation, TopFeature = this, BiomeHasFeature = biomeHasFeature },
            random, origin);

    private bool PlaceWithContext(PlacementContext context, IRandomSource random, Vector origin)
    {
        IEnumerable<Vector> positions = [origin];

        foreach (var modifier in this.Placement)
        {
            var current = positions;
            positions = current.SelectMany(position => modifier.GetPositions(context, random, position));
        }

        var placed = false;
        foreach (var position in positions)
        {
            var featureContext = new FeatureContext
            {
                Level = context.Level,
                Origin = position,
                Random = random,
                Generation = context.Generation,
                TopFeature = context.TopFeature
            };

            placed |= this.Feature.Place(featureContext);
        }

        return placed;
    }
}
