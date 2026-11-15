using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Features;

/// <summary>
/// Everything a configured feature needs to place itself, like vanilla's FeaturePlaceContext.
/// </summary>
public sealed class FeatureContext
{
    private Vector origin;

    /// <summary>
    /// Gets the world generation view used to read and place blocks.
    /// </summary>
    public required IWorldGenLevel Level { get; init; }

    /// <summary>
    /// Gets the feature placement origin in world block coordinates.
    /// </summary>
    public required Vector Origin { get => this.origin; init => this.origin = value; }

    /// <summary>
    /// Gets the random source shared by this placement's operations.
    /// </summary>
    public required IRandomSource Random { get; init; }

    /// <summary>
    /// Gets the vertical generation bounds used to resolve height providers and anchors.
    /// </summary>
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

    /// <summary>
    /// Moves the context to another origin, for a placement that places its feature at each of its positions in turn:
    /// features don't keep their context once they return.
    /// </summary>
    internal void MoveTo(Vector origin) => this.origin = origin;
}
