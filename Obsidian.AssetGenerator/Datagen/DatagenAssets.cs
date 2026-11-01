using System.Text.Json.Nodes;
using static Obsidian.AssetGenerator.Datagen.DatagenJson;

namespace Obsidian.AssetGenerator.Datagen;

/// <summary>
/// Writes the assets built from the vanilla data generators' output.
/// </summary>
/// <remarks>
/// The data generators write one file per registry entry; the assets combine them into one file per registry (or per
/// category, see <see cref="WorldgenAssets"/>) in the layout the source generators and the server read.
/// </remarks>
internal static class DatagenAssets
{
    // The registries of reports/registries.json written to enums.json, which become enums in Obsidian.API.
    private static readonly string[] enumRegistries =
        ["minecraft:data_component_type", "minecraft:map_decoration_type", "minecraft:mob_effect", "minecraft:potion",
         "minecraft:entity_type", "minecraft:particle_type", "minecraft:game_event"];

    // Container, block and supported entity tables evaluated by the server (see LootRegistryGenerator).
    private static readonly string[] lootTableCategories =
        ["archaeology", "blocks", "chests", "dispensers", "pots", "spawners", "entities", "gameplay"];

    /// <param name="generatedDirectory">The data generators' output (<c>data</c> and <c>reports</c>).</param>
    /// <param name="packetFieldsPath">The Java dumper's fields of each packet (see <see cref="JavaDumper.PacketFieldsPath"/>).</param>
    /// <param name="outputDirectory">The assets directory to write into.</param>
    /// <remarks>Worldgen comes from the legacy version for now (see <see cref="WriteLegacy"/>).</remarks>
    public static void Write(string generatedDirectory, string packetFieldsPath, string outputDirectory)
    {
        var reports = Path.Combine(generatedDirectory, "reports");
        var data = Path.Combine(generatedDirectory, "data", "minecraft");

        File.Copy(Path.Combine(reports, "blocks.json"), Path.Combine(outputDirectory, "blocks.json"), overwrite: true);
        // Each item's default components, { "minecraft:stone": { "components": { ... } } }, from one report per item.
        WriteJson(Path.Combine(outputDirectory, "item_components.json"),
            Combine(Path.Combine(reports, "minecraft", "components", "item"), ToId));

        WriteRegistries(ReadObject(Path.Combine(reports, "registries.json")), outputDirectory);
        WritePackets(ReadObject(Path.Combine(reports, "packets.json")), ReadObject(packetFieldsPath), outputDirectory);

        WriteJson(Path.Combine(outputDirectory, "recipes.json"), Combine(Path.Combine(data, "recipe"), path => path));
        WriteJson(Path.Combine(outputDirectory, "advancements.json"), Combine(Path.Combine(data, "advancement"), ToId));
        WriteJson(Path.Combine(outputDirectory, "enchantments.json"), Combine(Path.Combine(data, "enchantment"), ToId));
        WriteJson(Path.Combine(outputDirectory, "dialogs.json"), Combine(Path.Combine(data, "dialog"), ToId));
        WriteJson(Path.Combine(outputDirectory, "instruments.json"), Combine(Path.Combine(data, "instrument"), ToId));
        WriteLootTables(data, outputDirectory);
        foreach (var registry in new[] { "predicate", "context_int_provider", "context_float_provider" })
        {
            var definitions = Combine(Path.Combine(data, registry), ToId);
            WriteJson(Path.Combine(outputDirectory, registry + ".json"), definitions);
        }

        foreach (var registry in new[] { "block_transformer", "decorated_pot_pattern", "timeline" })
        {
            var definitions = Combine(Path.Combine(data, registry), ToId);
            WriteJson(Path.Combine(outputDirectory, registry + ".json"), definitions);
        }

        var tillTables = Combine(Path.Combine(data, "loot_table", "till"), path => ToId("till/" + path));
        WriteJson(Path.Combine(outputDirectory, "block_transformer_loot.json"), tillTables);

        WriteTags(Path.Combine(data, "tags"), outputDirectory);
        TradeAssets.Write(data, outputDirectory);
        CodecAssets.Write(data, outputDirectory);
    }

    private static void WriteLootTables(string dataDirectory, string outputDirectory)
    {
        foreach (var category in lootTableCategories)
        {
            var directory = Path.Combine(dataDirectory, "loot_table", category);
            var tables = Combine(directory, path => ToId($"{category}/{path}"));
            if (category == "entities")
            {
                // Keep the existing hard-coded drops for predicates the evaluator cannot model yet.
                var excluded = new[] { "ghast", "magma_cube", "slime", "turtle", "sheep", "pillager" };
                foreach (var id in excluded)
                {
                    tables.Remove($"minecraft:entities/{id}");
                }
            }

            if (category == "gameplay")
            {
                // Only fishing is evaluated so far; the other gameplay tables need their own owners.
                var excluded = tables.Select(table => table.Key)
                    .Where(id => !id.StartsWith("minecraft:gameplay/fishing", StringComparison.Ordinal))
                    .ToList();
                foreach (var id in excluded)
                {
                    tables.Remove(id);
                }
            }

            WriteJson(Path.Combine(outputDirectory, "loot_table", $"{category}.json"), tables);
        }
    }

    /// <summary>
    /// Writes the worldgen assets from an older vanilla version's data generators into
    /// <paramref name="outputDirectory"/> (<c>Assets/legacy-1.21.11</c>), in the layout the current version's would have:
    /// <c>worldgen</c> (see <see cref="WorldgenAssets"/>), <c>configured_carver</c> and <c>biome_parameters</c>.
    /// Natural spawning reads the current version's biomes' spawn attributes instead.
    /// </summary>
    /// <remarks>
    /// Temporary: the current version's worldgen data doesn't fit Obsidian's models yet, so worldgen runs on
    /// the older version's. Phase 5 (worldgen) of <c>docs/porting-26.3-plan.md</c> moves it to the
    /// current version and removes this. LegacyTags keeps worldgen tag references (including the worldgen helpers' tags)
    /// in the temporary obsidian_legacy namespace, with legacy contents and current registry ids, excluded from client
    /// sync. Remove that bridge with LegacyMinecraftVersion when Phase 5 is complete.
    /// </remarks>
    /// <param name="generatedDirectory">The legacy data generators' output (<c>data</c> and <c>reports</c>).</param>
    /// <param name="worldgenGroupsPath">
    /// The Java dumper's vanilla classes declaring each feature, of the current version: the legacy jar is obfuscated,
    /// so the dumper can't read it (see <see cref="WorldgenAssets"/>).
    /// </param>
    public static void WriteLegacy(string generatedDirectory, string worldgenGroupsPath,
        string outputDirectory)
    {
        var reports = Path.Combine(generatedDirectory, "reports");
        var data = Path.Combine(generatedDirectory, "data", "minecraft");

        CopyDirectory(Path.Combine(reports, "biome_parameters", "minecraft"), Path.Combine(outputDirectory, "biome_parameters"));

        // Test-only ids for the 1.21.11 worldgen captures. Phase 5 re-records them on the current version and removes
        // this table with the legacy assets. --all already includes the legacy blocks report.
        var states = ReadObject(Path.Combine(reports, "blocks.json"))
            .SelectMany(block => block.Value!["states"]!.AsArray().Select(state =>
            {
                var properties = state!["properties"]?.AsObject();
                var key = properties is null || properties.Count == 0 ? block.Key
                    : $"{block.Key}[{string.Join(',', properties.OrderBy(property => property.Key, StringComparer.Ordinal)
                        .Select(property => $"{property.Key}={property.Value!.GetValue<string>()}"))}]";
                return (Id: state["id"]!.GetValue<int>(), Key: key);
            }))
            .OrderBy(state => state.Id).ToArray();
        for (var id = 0; id < states.Length; id++)
        {
            if (states[id].Id != id)
                throw new InvalidDataException($"Legacy block state ids are not contiguous at {id}.");
        }

        WriteJson(Path.Combine(outputDirectory, "block_states.json"), new JsonArray([.. states.Select(state => (JsonNode?)state.Key)]));

        foreach (var (path, carver) in ReadAll(Path.Combine(data, "worldgen", "configured_carver")))
            WriteJson(Path.Combine(outputDirectory, "configured_carver", path + ".json"), carver);

        WorldgenAssets.Write(data, reports, worldgenGroupsPath, outputDirectory);
        LegacyTags.Write(data, outputDirectory);
    }

    /// <summary>A resource location of the <c>minecraft</c> namespace from its path.</summary>
    public static string ToId(string path) => $"minecraft:{path}";

    private static void WriteRegistries(JsonObject registries, string outputDirectory)
    {
        JsonObject Entries(string registry) => registries[registry]!["entries"]!.AsObject();

        // Fluids and argument types map straight to their protocol id.
        JsonObject ProtocolIds(string registry) =>
            new(Entries(registry).Select(entry => KeyValuePair.Create(entry.Key, (JsonNode?)entry.Value!["protocol_id"]!.DeepClone())));

        WriteJson(Path.Combine(outputDirectory, "items.json"), Entries("minecraft:item"));
        WriteJson(Path.Combine(outputDirectory, "sounds.json"), Entries("minecraft:sound_event"));
        WriteJson(Path.Combine(outputDirectory, "fluids.json"), ProtocolIds("minecraft:fluid"));
        WriteJson(Path.Combine(outputDirectory, "command_parsers.json"), ProtocolIds("minecraft:command_argument_type"));

        // Every built-in registry's entries in protocol id order, which the Obsidian client checks a NeoForge server's
        // registry sync against.
        var registryIds = new JsonObject();
        foreach (var (registry, _) in registries)
        {
            var names = Entries(registry)
                .OrderBy(entry => entry.Value!["protocol_id"]!.GetValue<int>())
                .Select(entry => (JsonNode?)entry.Key);

            registryIds[registry] = new JsonArray([.. names]);
        }

        WriteJson(Path.Combine(outputDirectory, "registry_ids.json"), registryIds);

        var enums = new JsonObject();
        foreach (var registry in enumRegistries)
            enums[registry] = Entries(registry).DeepClone();

        // Feed the dumper's attribute ids through the same asset as the API's other protocol-valued types.
        enums["minecraft:attribute"] = new JsonObject(ReadObject(Path.Combine(outputDirectory, "attribute_ids.json"))
            .Select(entry => KeyValuePair.Create(entry.Key, (JsonNode?)new JsonObject
            {
                ["protocol_id"] = entry.Value!.DeepClone()
            })));

        WriteJson(Path.Combine(outputDirectory, "enums.json"), enums);
    }

    /// <summary>
    /// Flattens the packets report (<c>state → direction → packet → protocol_id</c>) into the list the packet source
    /// generator reads, with the Java dumper's fields of each packet.
    /// </summary>
    private static void WritePackets(JsonObject report, JsonObject fields, string outputDirectory)
    {
        var packets = new JsonArray();
        foreach (var (state, directions) in report)
        {
            foreach (var (direction, statePackets) in directions!.AsObject())
            {
                foreach (var (id, packet) in statePackets!.AsObject())
                {
                    var resourceId = id[(id.IndexOf(':') + 1)..];
                    packets.Add(new JsonObject
                    {
                        ["name"] = ToPascalCase(resourceId),
                        ["resource_id"] = resourceId,
                        ["namespace"] = ToPascalCase(direction),
                        ["state"] = ToPascalCase(state),
                        ["packet_id"] = packet!["protocol_id"]!.DeepClone(),
                        ["usable_interface"] = $"I{ToPascalCase(direction)}Packet",
                        ["fields"] = (fields[direction]?[id]
                            ?? throw new InvalidDataException($"The Java dumper has no fields for {direction} {id}.")).DeepClone()
                    });
                }
            }
        }

        WriteJson(Path.Combine(outputDirectory, "packets.json"), packets);
    }

    /// <summary>
    /// Writes every tag as <c>"block/logs": { "name": "logs", "type": "block", "values": [...] }</c>, where the type is
    /// the tag's registry folder (<c>worldgen/biome</c>, <c>item/enchantable</c>).
    /// </summary>
    private static void WriteTags(string tagsDirectory, string outputDirectory)
    {
        var tags = new JsonObject();
        foreach (var (path, tag) in ReadAll(tagsDirectory))
        {
            var separator = path.LastIndexOf('/');
            tags[path] = new JsonObject
            {
                ["name"] = path[(separator + 1)..],
                ["type"] = path[..separator],
                ["values"] = tag["values"]!.DeepClone()
            };
        }

        WriteJson(Path.Combine(outputDirectory, "tags.json"), tags);
    }

    // clientbound → Clientbound, set_border_lerp_size → SetBorderLerpSize, debug/block_value → DebugBlockValue.
    private static string ToPascalCase(string path) =>
        string.Concat(path.Split(['_', '/'], StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}
