using System.Text.Json.Nodes;
using static Obsidian.AssetGenerator.Datagen.DatagenJson;

namespace Obsidian.AssetGenerator.Datagen;

/// <summary>
/// Temporary tag bridge for WriteLegacy (Phases 4/5). Keep legacy membership and order, but let the source generator
/// resolve entry identifiers to the current registry ids. The server-only definitions never enter client categories.
/// </summary>
internal static class LegacyTags
{
    private const string LegacyNamespace = "obsidian_legacy";

    // Tags used by worldgen code rather than JSON, including tree parts in the API and structure post-processing.
    private static readonly string[] blockTags =
    [
        "bamboo_plantable_on", "base_stone_overworld", "big_dripleaf_placeable", "coral_blocks", "corals", "dirt",
        "dripstone_replaceable_blocks", "dry_vegetation_may_place_on", "features_cannot_replace", "fences", "fire",
        "ice", "jungle_logs", "lava_pool_stone_cannot_replace", "leaves", "logs", "mushroom_grow_block", "nylium",
        "replaceable_by_mushrooms", "replaceable_by_trees", "sand", "sculk_replaceable", "sculk_replaceable_world_gen",
        "shulker_boxes", "slabs", "small_dripleaf_placeable", "snow", "snow_layer_can_survive_on",
        "snow_layer_cannot_survive_on", "stairs", "wall_corals", "wall_hanging_signs", "wall_post_override", "walls",
        "wooden_fences"
    ];

    public static void Write(string legacyData, string outputDirectory)
    {
        var tagsPath = Path.Combine(Path.GetDirectoryName(outputDirectory)!, "tags.json");
        var tags = ReadObject(tagsPath);

        string Preserve(string registry, string identifier)
        {
            var path = identifier[(identifier.IndexOf(':') + 1)..];
            var key = $"{registry}/{LegacyNamespace}/{path}";
            if (!tags.ContainsKey(key))
            {
                var separator = path.LastIndexOf('/');
                tags[key] = new JsonObject
                {
                    ["name"] = path[(separator + 1)..],
                    ["type"] = separator < 0 ? registry : $"{registry}/{path[..separator]}",
                    ["namespace"] = LegacyNamespace,
                    ["server_only"] = true,
                    ["values"] = new JsonArray([.. Resolve(legacyData, registry, path).Select(id => (JsonNode?)id)])
                };
            }

            return $"{LegacyNamespace}:{path}";
        }

        foreach (var tag in blockTags)
            Preserve("block", tag);
        foreach (var tag in new[] { "mineshaft_blocking", "required_ocean_monument_surrounding" })
            Preserve("worldgen/biome", tag);

        // Transform the written assets, after grouping/categorisation used their original vanilla identifiers.
        // Holder sets keep their shape; plain tag fields (tag_match, matching_block_tag) are rewritten too.
        void Rewrite(JsonNode? node, string registry = "block")
        {
            if (node is JsonObject obj)
            {
                foreach (var (key, value) in obj.ToArray())
                {
                    var childRegistry = key switch
                    {
                        "biomes" or "preferred_biomes" => "worldgen/biome",
                        _ => registry
                    };
                    if (value is JsonValue scalar && scalar.TryGetValue(out string? text))
                        obj[key] = RewriteString(text, childRegistry, key == "tag");
                    else
                        Rewrite(value, childRegistry);
                }
            }
            else if (node is JsonArray array)
            {
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue scalar && scalar.TryGetValue(out string? text))
                        array[i] = RewriteString(text, registry, false);
                    else
                        Rewrite(array[i], registry);
                }
            }
        }

        string RewriteString(string value, string registry, bool plainTag)
        {
            if (!plainTag && !value.StartsWith("#minecraft:", StringComparison.Ordinal))
                return value; // Includes biome colour literals such as #3f76e4.

            var identifier = plainTag ? value : value[1..];
            return (plainTag ? "" : "#") + Preserve(registry, identifier);
        }

        foreach (var directory in new[] { "worldgen", "configured_carver" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(outputDirectory, directory), "*.json", SearchOption.AllDirectories))
            {
                // Structure tags already have their own legacy resolver (StructureTags), without numeric ids.
                if (Path.GetFileName(file) == "structure_tags.json")
                    continue;

                var json = Read(file);
                Rewrite(json);
                WriteJson(file, json);
            }
        }

        WriteJson(tagsPath, tags);
    }

    // Vanilla tag order: expand nested tags in declaration order and keep only the first occurrence of an entry.
    private static IEnumerable<string> Resolve(string data, string registry, string identifier)
    {
        var path = identifier[(identifier.IndexOf(':') + 1)..];
        var file = Path.Combine(data, "tags", registry, path + ".json");
        if (!File.Exists(file))
            throw new InvalidDataException($"Missing legacy worldgen tag {registry}/{identifier} in {data}.");

        return Read(file)["values"]!.AsArray().SelectMany(value =>
        {
            var id = value is JsonObject entry ? entry["id"]!.GetValue<string>() : value!.GetValue<string>();
            return id.StartsWith('#') ? Resolve(data, registry, id[1..]) : [id];
        }).Distinct(StringComparer.Ordinal);
    }
}
