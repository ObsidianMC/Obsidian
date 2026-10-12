using Obsidian.WorldData.Structures;
using System.Text.Json;
using static Obsidian.Registries.Codecs.CodecJson;

namespace Obsidian.Registries.Codecs;

/// <summary>
/// A NeoForge biome modifier from a plugin's <c>data/&lt;namespace&gt;/neoforge/biome_modifier</c> folder, in
/// NeoForge's format: <c>neoforge:add_features</c> (<c>biomes</c>, <c>features</c>, <c>step</c>),
/// <c>neoforge:remove_features</c> (<c>biomes</c>, <c>features</c>, optional <c>steps</c>) or <c>neoforge:none</c>.
/// </summary>
/// <remarks>
/// <c>biomes</c> and <c>features</c> are holder sets: an identifier, a list of them or a <c>#tag</c> (biome tags
/// only). Features added can also be defined inline. References are checked when the modifier is applied (see
/// <see cref="PluginFeatures"/>), since they may be other plugins' entries.
/// </remarks>
/// <param name="Type">The modifier type, like <c>neoforge:add_features</c>.</param>
/// <param name="Biomes">Biome identifiers, or a single <c>#tag</c>.</param>
/// <param name="Features">Placed features: identifiers as JSON strings, or objects defined inline.</param>
/// <param name="Steps">The decoration steps added to or removed from.</param>
internal sealed record BiomeModifierData(
    string Type,
    ImmutableArray<string> Biomes,
    ImmutableArray<JsonElement> Features,
    ImmutableArray<DecorationStep> Steps)
{
    public const string AddFeatures = "neoforge:add_features";

    public const string RemoveFeatures = "neoforge:remove_features";

    public const string None = "neoforge:none";

    // Vanilla's GenerationStep.Decoration names, in DecorationStep's order.
    private static readonly string[] stepNames = System.Enum.GetNames<DecorationStep>()
        .Select(name => name.ToSnakeCase())
        .ToArray();

    public static BiomeModifierData Read(JsonElement root)
    {
        Object(root, "");

        var type = Identifier(Required(root, "", "type"), "type");

        switch (type)
        {
            case None:
                return new BiomeModifierData(type, [], [], []);
            case AddFeatures:
                return new BiomeModifierData(
                    type,
                    ReadBiomes(Required(root, "", "biomes"), "biomes"),
                    ReadFeatures(Required(root, "", "features"), "features", inline: true),
                    [ReadStep(Required(root, "", "step"), "step")]);
            case RemoveFeatures:
                var steps = TryGet(root, "steps", out var value)
                    ? ReadSteps(value, "steps")
                    : [.. System.Enum.GetValues<DecorationStep>()];

                return new BiomeModifierData(
                    type,
                    ReadBiomes(Required(root, "", "biomes"), "biomes"),
                    ReadFeatures(Required(root, "", "features"), "features", inline: false),
                    steps);
            default:
                throw Error("type", $"{type} isn't supported yet: plugins can use {AddFeatures}, {RemoveFeatures} "
                    + $"and {None}");
        }
    }

    private static ImmutableArray<string> ReadBiomes(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.String && value.GetString()!.StartsWith('#'))
        {
            var tag = Tag(value, path);
            return TagsRegistry.TryGet("worldgen/biome", tag, out _)
                ? [$"#{tag}"]
                : throw Error(path, $"#{tag} isn't a biome tag");
        }

        if (value.ValueKind == JsonValueKind.Object)
            throw Error(path, "expected a biome, a list of biomes or a #tag; other holder sets aren't supported");

        if (value.ValueKind != JsonValueKind.Array)
            return [Identifier(value, path)];

        return [.. value.EnumerateArray().Select((biome, i) => Identifier(biome, Index(path, i)))];
    }

    // Placed features as JSON: identifiers (normalised) or, where allowed, inline objects.
    private static ImmutableArray<JsonElement> ReadFeatures(JsonElement value, string path, bool inline)
    {
        if (value.ValueKind == JsonValueKind.String && value.GetString()!.StartsWith('#'))
            throw Error(path, "placed feature tags aren't supported; list the features");

        if (value.ValueKind != JsonValueKind.Array)
            return [ReadFeature(value, path, inline)];

        return [.. value.EnumerateArray().Select((feature, i) => ReadFeature(feature, Index(path, i), inline))];
    }

    private static JsonElement ReadFeature(JsonElement value, string path, bool inline)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            return inline
                ? value.Clone()
                : throw Error(path, "a feature defined inline is in no biome; refer to one by its identifier");
        }

        return JsonSerializer.SerializeToElement(Identifier(value, path));
    }

    private static ImmutableArray<DecorationStep> ReadSteps(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return [ReadStep(value, path)];

        return [.. value.EnumerateArray().Select((step, i) => ReadStep(step, Index(path, i)))];
    }

    private static DecorationStep ReadStep(JsonElement value, string path) =>
        (DecorationStep)System.Array.IndexOf(stepNames, Enum(value, path, stepNames));
}
