namespace Obsidian.API.World.Features;

/// <summary>
/// A feature type together with its settings, like vanilla's ConfiguredFeature.
/// </summary>
/// <remarks>
/// Implementations are data-driven: the source generator instantiates them from
/// <c>Assets/worldgen/features/*.json</c> and exposes them through <c>ConfiguredFeatures</c>.
/// </remarks>
public abstract class ConfiguredFeatureBase : IWorldFeature
{
    public abstract string Type { get; }

    /// <summary>
    /// Registry id (e.g. <c>minecraft:oak</c>), or empty for features defined inline.
    /// </summary>
    public string Identifier { get; init; } = string.Empty;

    /// <summary>
    /// Places the feature at <see cref="FeatureContext.Origin"/>. Returns whether anything was placed.
    /// </summary>
    public abstract bool Place(FeatureContext context);
}
