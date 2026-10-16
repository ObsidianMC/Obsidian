using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Obsidian.Registries.Codecs;

/// <summary>
/// Turns the dimension type and biomes a level stem defines inline, as vanilla's stem codec allows, into entries of
/// their own, so that they get ids and are sent to clients like those of files.
/// </summary>
/// <remarks>
/// <para>
/// The entries are named for the stem <c>&lt;namespace&gt;:&lt;path&gt;</c>: its dimension type is
/// <c>&lt;namespace&gt;:&lt;path&gt;/type</c> and its biomes <c>&lt;namespace&gt;:&lt;path&gt;/biome_&lt;n&gt;</c>,
/// numbered from 0 in the order they're found: the fixed source's <c>biome</c>, the checkerboard's <c>biomes</c> or the
/// multi-noise list's, in their order, or the flat generator's <c>biome</c>. They're read with the plugin's or data
/// pack's files like a file of that identifier would be (see <see cref="PluginCodecs"/>), and the stem names them in
/// their place. Vanilla gives inline entries no identifier; clients only need one to be sent them.
/// </para>
/// <para>
/// A reload changing an inline entry changes the stem too, so the restart rules of both apply. A file of the same
/// identifier, like <c>dimension_type/&lt;path&gt;/type.json</c>, competes with the inline entry like two files.
/// </para>
/// </remarks>
internal static class LevelStemInline
{
    // Gson reads data packs leniently, so comments and trailing commas are fine.
    private static readonly JsonDocumentOptions documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Finds the entries a level stem defines inline.
    /// </summary>
    /// <param name="identifier">The stem's identifier, like <c>ruby:crystal_world</c>.</param>
    /// <param name="json">The stem file's bytes.</param>
    /// <returns>The stem naming the entries instead, or the bytes given when it defines none or isn't JSON (its own
    /// read reports that), and the entries.</returns>
    public static (byte[] Stem, ImmutableArray<InlineEntry> Entries) Extract(string identifier, byte[] json)
    {
        JsonNode? root;

        try
        {
            root = JsonNode.Parse(json, documentOptions: documentOptions);
        }
        catch (JsonException)
        {
            return (json, []);
        }

        if (root is not JsonObject stem)
            return (json, []);

        var entries = ImmutableArray.CreateBuilder<InlineEntry>();
        var biomes = 0;

        // Keeps an inline entry, and gives the identifier the stem names it by in its place.
        JsonValue Name(JsonObject entry, string registry, string name)
        {
            entries.Add(new InlineEntry(registry, name, Encoding.UTF8.GetBytes(entry.ToJsonString())));
            return JsonValue.Create(name);
        }

        JsonValue Biome(JsonObject entry) => Name(entry, PluginCodecs.Biome, $"{identifier}/biome_{biomes++}");

        if (stem["type"] is JsonObject type)
            stem["type"] = Name(type, PluginCodecs.DimensionType, $"{identifier}/type");

        if (stem["generator"] is JsonObject generator)
        {
            switch (TypeOf(generator))
            {
                case "minecraft:noise" when generator["biome_source"] is JsonObject source:
                    BiomeSource(source, Biome);
                    break;
                case "minecraft:flat" when generator["settings"] is JsonObject settings:
                    if (settings["biome"] is JsonObject biome)
                        settings["biome"] = Biome(biome);

                    break;
            }
        }

        return entries.Count == 0
            ? (json, [])
            : (Encoding.UTF8.GetBytes(stem.ToJsonString()), entries.ToImmutable());
    }

    // The biomes a vanilla biome source may define inline.
    private static void BiomeSource(JsonObject source, Func<JsonObject, JsonValue> biome)
    {
        switch (TypeOf(source))
        {
            case "minecraft:fixed":
                if (source["biome"] is JsonObject fixedBiome)
                    source["biome"] = biome(fixedBiome);

                break;
            case "minecraft:checkerboard":
                if (source["biomes"] is JsonObject single)
                    source["biomes"] = biome(single);
                else if (source["biomes"] is JsonArray list)
                    ReplaceAll(list, biome);

                break;
            case "minecraft:multi_noise" when source["biomes"] is JsonArray points:
                foreach (var point in points)
                {
                    if (point is JsonObject entry && entry["biome"] is JsonObject pointBiome)
                        entry["biome"] = biome(pointBiome);
                }

                break;
        }
    }

    private static void ReplaceAll(JsonArray list, Func<JsonObject, JsonValue> biome)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is JsonObject entry)
                list[i] = biome(entry);
        }
    }

    // An object's type, with vanilla's default namespace, or null.
    private static string? TypeOf(JsonObject value) =>
        value["type"] is JsonValue type
        && type.TryGetValue<string>(out var text)
        && CodecJson.TryParseIdentifier(text, out var identifier)
            ? identifier
            : null;
}

/// <summary>
/// A dimension type or biome a level stem defines inline, as the file its identifier would name.
/// </summary>
/// <param name="Registry"><see cref="PluginCodecs.DimensionType"/> or <see cref="PluginCodecs.Biome"/>.</param>
/// <param name="Identifier">The identifier it's given (see <see cref="LevelStemInline"/>).</param>
/// <param name="Json">The entry's JSON.</param>
internal sealed record InlineEntry(string Registry, string Identifier, byte[] Json);
