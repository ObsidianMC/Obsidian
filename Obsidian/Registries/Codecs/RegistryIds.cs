using Obsidian.Entities;
using System.Collections.Frozen;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.Registries.Codecs;

/// <summary>
/// The entries of the registries plugins' data files refer to, so a reference to a missing entry is rejected like
/// vanilla rejects one. Identifiers have their namespace, like <c>minecraft:ambient.cave</c>.
/// </summary>
internal static class RegistryIds
{
    private static readonly Lazy<FrozenSet<string>> soundEvents = new(() => Keys("Obsidian.Assets.sounds.json"));

    private static readonly Lazy<FrozenSet<string>> particleTypes = new(ReadParticleTypes);

    // Vanilla 26.3's particle types whose options are more than their type (vanilla's ParticleTypes registers
    // them with an options codec), which plugins' files can't give yet.
    private static readonly FrozenSet<string> particlesWithOptions = new[]
    {
        "block", "block_marker", "block_crumble", "dragon_breath", "dust", "dust_color_transition", "dust_pillar",
        "effect", "entity_effect", "falling_dust", "flash", "instant_effect", "item", "sculk_charge", "shriek",
        "tinted_leaves", "trail", "vibration", "geyser", "geyser_plume", "geyser_base", "geyser_poof"
    }.Select(name => $"minecraft:{name}").ToFrozenSet();

    // Vanilla 1.21.11's activities (its Activity registry), which villagers' environment attributes take.
    private static readonly FrozenSet<string> activities = new[]
    {
        "core", "idle", "work", "play", "rest", "meet", "panic", "raid", "pre_raid", "hide", "fight", "celebrate",
        "admire_item", "avoid", "ride", "play_dead", "long_jump", "ram", "tongue", "swim", "lay_spawn", "sniff",
        "investigate", "roar", "emerge", "dig"
    }.Select(name => $"minecraft:{name}").ToFrozenSet();

    // Vanilla's placed features, the keys of Obsidian's placed feature assets.
    private static readonly Lazy<FrozenSet<string>> placedFeatures = new(() => AssetNames("worldgen.placed_features")
        .SelectMany(Keys)
        .Concat(CurrentReferences("placed_feature"))
        .ToFrozenSet());

    // Vanilla's configured carvers, one asset each.
    private static readonly Lazy<FrozenSet<string>> configuredCarvers = new(ReadConfiguredCarvers);

    public static bool IsSoundEvent(string identifier) => soundEvents.Value.Contains(identifier);

    public static bool IsParticleType(string identifier) => particleTypes.Value.Contains(identifier);

    public static bool HasOptions(string particleType) => particlesWithOptions.Contains(particleType);

    public static bool IsActivity(string identifier) => activities.Contains(identifier);

    public static bool IsPlacedFeature(string identifier) => placedFeatures.Value.Contains(identifier);

    public static bool IsConfiguredCarver(string identifier) => configuredCarvers.Value.Contains(identifier);

    // Terrain still uses legacy feature assets during Phase 5. Format-121 readers also recognize the current
    // registry's identifiers, including entries not referenced by vanilla biomes.
    private static IEnumerable<string> CurrentReferences(string registry)
    {
        var resource = $"Obsidian.Assets.data_pack.{registry}_ids.json";
        using var stream = typeof(RegistryIds).Assembly.GetManifestResourceStream(resource)!;
        using var document = JsonDocument.Parse(stream);

        foreach (var identifier in document.RootElement.EnumerateArray())
        {
            yield return identifier.GetString()!;
        }
    }

    /// <summary>
    /// Whether an entity type is vanilla's or a loaded plugin's.
    /// </summary>
    public static bool IsEntityType(string identifier) =>
        (EntityNbt.TryParseType(identifier, out var type) && EntityNbt.TypeId(type) == identifier)
        || EntityDefinitionsRegistry.TryGet(identifier, out _);

    private static FrozenSet<string> ReadParticleTypes() => Enum.GetNames<ParticleType>()
        .Select(name => $"minecraft:{name.ToSnakeCase()}")
        .ToFrozenSet();

    private static FrozenSet<string> ReadConfiguredCarvers() => AssetNames("configured_carver")
        .Select(resource => $"minecraft:{Path.GetFileNameWithoutExtension(resource).Split('.')[^1]}")
        .Concat(CurrentReferences("carver"))
        .ToFrozenSet();

    // The keys of a JSON object asset, like the identifiers of sounds.json.
    private static FrozenSet<string> Keys(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The asset {resource} is missing.");
        using var document = JsonDocument.Parse(stream);

        return document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .ToFrozenSet();
    }

    private static IEnumerable<string> AssetNames(string folder)
    {
        var prefix = $"Obsidian.Assets.{folder}.";

        return Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Where(name => name.EndsWith(".json", StringComparison.Ordinal));
    }
}
