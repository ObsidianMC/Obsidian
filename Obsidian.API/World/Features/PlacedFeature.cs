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

    // Built-in features don't keep their context once they return, so a placement moves one context from position to
    // position; features from other assemblies may keep theirs, so they get a new context per position.
    private bool? reusesFeatureContext;
    private bool ReusesFeatureContext => this.reusesFeatureContext ??= this.Feature.GetType().Assembly.GetName().Name is "Obsidian" or "Obsidian.API";

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
        FeatureContext? featureContext = null;
        return this.PlaceFrom(0, context, random, origin, ref featureContext);
    }

    /// <summary>
    /// Runs the modifiers from <paramref name="modifier"/> on a position and places the feature at each resulting position.
    /// </summary>
    /// <remarks>
    /// Like vanilla's chained streams, each position goes through the remaining modifiers and gets placed before the
    /// modifier that produced it is asked for the next one, which decides the order random numbers are drawn in.
    /// </remarks>
    /// <param name="featureContext">The context the feature was placed with so far, moved to each next position.</param>
    private bool PlaceFrom(int modifier, PlacementContext context, IRandomSource random, Vector position, ref FeatureContext? featureContext)
    {
        if (modifier == this.Placement.Length)
        {
            if (featureContext is null || !this.ReusesFeatureContext)
            {
                featureContext = new FeatureContext
                {
                    Level = context.Level,
                    Origin = position,
                    Random = random,
                    Generation = context.Generation,
                    TopFeature = context.TopFeature
                };
            }
            else
            {
                featureContext.MoveTo(position);
            }

            return this.Feature.Place(featureContext);
        }

        var placement = this.Placement[modifier];
        if (placement is SinglePlacementModifierBase single)
        {
            return single.GetPosition(context, random, position) is Vector next
                && this.PlaceFrom(modifier + 1, context, random, next, ref featureContext);
        }

        var placed = false;
        foreach (var next in placement.GetPositions(context, random, position))
            placed |= this.PlaceFrom(modifier + 1, context, random, next, ref featureContext);

        return placed;
    }
}
