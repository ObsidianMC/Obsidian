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
/// A modifier that gives at most one position: it moves the position, or drops it.
/// </summary>
/// <remarks>
/// Most modifiers are like this; <see cref="PlacedFeature"/> places through <see cref="GetPosition"/> without building a
/// sequence for every position.
/// </remarks>
public abstract class SinglePlacementModifierBase : PlacementModifierBase
{
    public sealed override IEnumerable<Vector> GetPositions(PlacementContext context, IRandomSource random, Vector position) =>
        this.GetPosition(context, random, position) is Vector next ? [next] : [];

    /// <summary>
    /// The position that replaces <paramref name="position"/>, or <c>null</c> to drop it.
    /// </summary>
    public abstract Vector? GetPosition(PlacementContext context, IRandomSource random, Vector position);
}

/// <summary>
/// A modifier that keeps or drops the position, like vanilla's PlacementFilter.
/// </summary>
public abstract class PlacementFilterBase : SinglePlacementModifierBase
{
    public sealed override Vector? GetPosition(PlacementContext context, IRandomSource random, Vector position) =>
        this.ShouldPlace(context, random, position) ? position : null;

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
