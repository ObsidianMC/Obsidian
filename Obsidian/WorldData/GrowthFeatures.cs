using System.Text.Json;
using Microsoft.Extensions.Logging;
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData;

/// <summary>
/// Where plants that grow into a feature find and place it: saplings their trees, mushrooms, fungi, nylium and moss
/// what bone meal grows from them (see <see cref="Growth"/>). Like vanilla's registry lookups, features are found by
/// identifier when the plant grows, among the plugin feature registry's configured features (vanilla's, overridden by
/// plugins' and data packs'), so that a reload or override applies at once.
/// </summary>
/// <remarks>
/// A level holds one (<see cref="AbstractLevel.GrowthFeatures"/>); tests give theirs one that records what it's asked
/// to place.
/// </remarks>
internal partial class GrowthFeatures
{
    /// <summary>
    /// The plugin feature registry's configured features.
    /// </summary>
    public static GrowthFeatures Registered { get; } = new();

    /// <summary>
    /// The configured feature of an identifier, or null when there's none: vanilla checks a feature is there before
    /// clearing the plant for it.
    /// </summary>
    public virtual ConfiguredFeatureBase? Find(string identifier) =>
        PluginFeatures.CurrentRegistry.FeatureSet!.FindConfigured(identifier);

    /// <summary>
    /// The trunk's base height of the tree an identifier names (vanilla's
    /// <c>TreeFeature.trunkPlacer().getBaseHeight()</c>), or null when it isn't a tree.
    /// </summary>
    public virtual int? TrunkBaseHeight(string identifier) =>
        Definition(identifier, "minecraft:tree") is JsonElement tree
        && tree.TryGetProperty("trunk_placer", out var trunk)
        && trunk.TryGetProperty("base_height", out var height)
            ? height.GetInt32()
            : null;

    /// <summary>
    /// The cap radius of the huge mushroom an identifier names (vanilla's
    /// <c>AbstractHugeMushroomFeature.foliageRadius()</c>, 2 unless it says otherwise), or null when it isn't one.
    /// </summary>
    public virtual int? MushroomFoliageRadius(string identifier)
    {
        var mushroom = Definition(identifier, "minecraft:huge_brown_mushroom")
            ?? Definition(identifier, "minecraft:huge_red_mushroom");
        if (mushroom is not JsonElement definition)
            return null;

        return definition.TryGetProperty("foliage_radius", out var radius) ? radius.GetInt32() : 2;
    }

    // The feature's definition, when it's of the type.
    private static JsonElement? Definition(string identifier, string type) =>
        PluginFeatures.CurrentRegistry.FeatureDefinition(identifier) is JsonElement definition
        && definition.TryGetProperty("type", out var given)
        && given.GetString() == type
            ? definition
            : null;

    /// <summary>
    /// Places the configured feature of an identifier at <paramref name="origin"/>, which fails rather than stopping
    /// the level when a plugin's feature throws.
    /// </summary>
    /// <returns>Whether the feature was found and placed something.</returns>
    public virtual bool Place(string identifier, LiveFeatureLevel level, Vector origin, IRandomSource random)
    {
        var features = PluginFeatures.CurrentRegistry.FeatureSet!;

        try
        {
            return features.PlaceConfigured(identifier, new FeatureContext
            {
                Level = level,
                Origin = origin,
                Random = random,
                Generation = level.Generation
            });
        }
        catch (PluginGenerationException exception)
        {
            Log.FeatureFailed(level.Logger, exception, identifier);
            return false;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "The feature {Feature} a plant grows into failed to grow.")]
        public static partial void FeatureFailed(ILogger logger, Exception exception, string feature);
    }
}
