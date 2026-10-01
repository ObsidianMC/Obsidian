using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Features;

/// <summary>
/// Everything a configured feature needs to place itself, like vanilla's FeaturePlaceContext.
/// </summary>
public sealed class FeatureContext
{
    public required IWorldGenLevel Level { get; init; }

    public required Vector Origin { get; init; }

    public required IRandomSource Random { get; init; }

    public required WorldGenerationContext Generation { get; init; }

    /// <summary>
    /// The placed feature that started this placement, if any. Features that place other features pass it along.
    /// </summary>
    public PlacedFeature? TopFeature { get; init; }

    /// <summary>
    /// A copy of this context with a different origin.
    /// </summary>
    public FeatureContext At(Vector origin) => new()
    {
        Level = this.Level,
        Origin = origin,
        Random = this.Random,
        Generation = this.Generation,
        TopFeature = this.TopFeature
    };
}
