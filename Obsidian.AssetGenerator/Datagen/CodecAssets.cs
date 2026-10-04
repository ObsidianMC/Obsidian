using System.Text.Json.Nodes;
using static Obsidian.AssetGenerator.Datagen.DatagenJson;

namespace Obsidian.AssetGenerator.Datagen;

/// <summary>
/// Writes <c>Codecs/</c>: the synchronized datapack registries the server sends in its registry data, as
/// <c>{ "type": registry, "value": [{ "name", "id", "element" }] }</c> with ids in vanilla's (sorted) order.
/// </summary>
internal static class CodecAssets
{
    // Asset file name → registry folder under data/minecraft (also the registry's name).
    private static readonly (string File, string Registry)[] codecs =
    [
        ("biomes", "worldgen/biome"),
        ("cat_variant", "cat_variant"),
        ("chat_type", "chat_type"),
        ("chicken_variant", "chicken_variant"),
        ("cow_variant", "cow_variant"),
        ("damage_type", "damage_type"),
        ("dialogs", "dialog"),
        ("dimensions", "dimension_type"),
        ("frog_variant", "frog_variant"),
        ("painting_variant", "painting_variant"),
        ("pig_variant", "pig_variant"),
        ("trim_material", "trim_material"),
        ("trim_pattern", "trim_pattern"),
        ("wolf_sound_variant", "wolf_sound_variant"),
        ("wolf_variant", "wolf_variant"),
        ("zombie_nautilus_variant", "zombie_nautilus_variant"),
    ];

    // The codec source generator emits an initializer per element property for the codec classes of
    // Obsidian.API/Registry/Codecs, so these elements only keep the properties those classes model (in their order).
    // The rest (such as the environment attributes 1.21.11 moved biome and dimension effects to) isn't sent yet.
    private static readonly Dictionary<string, string[]> modeledProperties = new()
    {
        ["biomes"] =
        [
            "effects", "depth", "temperature", "scale", "downfall", "category", "has_precipitation", "temperature_modifier",
            "player_spawn_friendly", "features", "carvers", "spawners", "spawn_costs"
        ],
        ["dimensions"] =
        [
            "monster_spawn_block_light_limit", "monster_spawn_light_level", "piglin_safe", "natural", "ambient_light", "fixed_time",
            "infiniburn", "respawn_anchor_works", "has_skylight", "bed_works", "effects", "has_raids", "min_y", "height",
            "logical_height", "coordinate_scale", "ultrawarm", "has_ceiling"
        ],
        ["painting_variant"] = ["asset_id", "height", "width"],
    };

    // The properties BiomeEffect models of a biome's effects.
    private static readonly string[] modeledBiomeEffects =
        ["grass_color_modifier", "ambient_sound", "particle", "foliage_color", "sky_color", "water_fog_color", "fog_color", "water_color", "grass_color"];

    public static void Write(string dataDirectory, string outputDirectory)
    {
        foreach (var (file, registry) in codecs)
        {
            var entries = new JsonArray();
            foreach (var (path, json) in ReadAll(Path.Combine(dataDirectory, registry)))
            {
                var element = json.AsObject();
                if (modeledProperties.TryGetValue(file, out var properties))
                    element = Select(element, properties);

                if (file == "biomes" && element["effects"] is JsonObject effects)
                    element["effects"] = Select(effects, modeledBiomeEffects);

                entries.Add(new JsonObject
                {
                    ["name"] = DatagenAssets.ToId(path),
                    ["id"] = entries.Count,
                    ["element"] = element
                });
            }

            var codec = new JsonObject
            {
                ["type"] = DatagenAssets.ToId(registry),
                ["value"] = entries
            };

            DatagenJson.WriteJson(Path.Combine(outputDirectory, "Codecs", $"{file}.json"), codec);
        }
    }

    /// <summary>The <paramref name="properties"/> <paramref name="obj"/> has, in that order.</summary>
    private static JsonObject Select(JsonObject obj, string[] properties)
    {
        var selected = new JsonObject();
        foreach (var name in properties)
        {
            if (obj.TryGetPropertyValue(name, out var value))
            {
                obj.Remove(name);
                selected[name] = value;
            }
        }

        return selected;
    }
}
