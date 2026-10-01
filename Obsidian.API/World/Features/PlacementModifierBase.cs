using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Features;

/// <summary>
/// Turns one candidate position into zero or more positions (counts, spreads, filters), like vanilla's PlacementModifier.
/// </summary>
public abstract class PlacementModifierBase
{
    public abstract string Type { get; }

    public abstract IEnumerable<Vector> GetPositions(PlacementContext context, IRandomSource random, Vector position);
}

/// <summary>
/// A modifier that keeps or drops the position, like vanilla's PlacementFilter.
/// </summary>
public abstract class PlacementFilterBase : PlacementModifierBase
{
    public sealed override IEnumerable<Vector> GetPositions(PlacementContext context, IRandomSource random, Vector position) =>
        this.ShouldPlace(context, random, position) ? [position] : [];

    protected abstract bool ShouldPlace(PlacementContext context, IRandomSource random, Vector position);
}

/// <summary>
/// State shared by the placement modifiers of one placed feature.
/// </summary>
public sealed class PlacementContext
{
    public required IWorldGenLevel Level { get; init; }

    public required WorldGenerationContext Generation { get; init; }

    /// <summary>
    /// The placed feature being decorated, used by biome filters; <c>null</c> when placed without a biome check.
    /// </summary>
    public PlacedFeature? TopFeature { get; init; }

    /// <summary>
    /// Whether a biome lists a placed feature in its decoration steps; used by biome filters.
    /// </summary>
    public Func<BiomeCodec, PlacedFeature, bool>? BiomeHasFeature { get; init; }
}
